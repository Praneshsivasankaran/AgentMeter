import Darwin
import Foundation

struct Installation: Sendable, Equatable {
  let executable: URL
  let activityExecutable: URL
  let version: String
}
actor ProviderDiscovery {
  private var cache: [ProviderID: (String, Date, Installation)] = [:]
  let directory: URL
  private let paths: [String]
  init(directory: URL? = nil, searchDirectories: [String]? = nil) {
    self.paths = searchDirectories ?? Self.searchDirectories
    let work = directory ?? RuntimePaths.providerWork
    try? FileManager.default.createDirectory(
      at: work, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
    self.directory = RuntimePaths.canonicalDirectory(work)
  }
  nonisolated static var searchDirectories: [String] {
    let h = FileManager.default.homeDirectoryForCurrentUser.path
    let known = [
      "/opt/homebrew/bin", "/usr/local/bin", h + "/.local/bin", h + "/.npm-global/bin",
      h + "/.npm/bin", "/usr/bin", "/bin",
    ]
    let inherited = (getenv("PATH").map { String(cString: $0) } ?? "").split(separator: ":").prefix(
      32
    ).map(String.init).filter { $0.hasPrefix("/") && $0.count < 4096 }
    return (known + inherited).filter { RuntimePaths.permitsDiscovery($0) }.reduce(into: []) {
      a, v in if !a.contains(v) { a.append(v) }
    }
  }
  nonisolated var childEnvironment: [String] {
    [
      "PATH=" + Self.searchDirectories.joined(separator: ":"), "PWD=" + directory.path,
      "GIT_CEILING_DIRECTORIES=" + directory.deletingLastPathComponent().path,
      "GIT_CONFIG_NOSYSTEM=1", "GIT_CONFIG_GLOBAL=/dev/null",
    ]
  }
  func find(_ provider: ProviderID) async throws -> Installation {
    let fm = FileManager.default
    for directory in paths {
      let alias = URL(fileURLWithPath: directory).appendingPathComponent(provider.rawValue)
      guard let resolved = RuntimePaths.discoveryExecutable(alias),
        fm.isExecutableFile(atPath: resolved.path)
      else { continue }
      let modified =
        (try? resolved.resourceValues(forKeys: [.contentModificationDateKey])
          .contentModificationDate) ?? .distantPast
      if let c = cache[provider], c.0 == resolved.path, c.1 == modified,
        fm.isExecutableFile(atPath: c.2.activityExecutable.path)
      {
        return c.2
      }
      var limits = ProcessLimits()
      limits.seconds = 5
      limits.stdout = 4096
      limits.stderr = 4096
      limits.line = 4096
      let (data, status) = try await Subprocess.run(
        executable: alias, arguments: ["--version"], environment: childEnvironment,
        directory: self.directory, limits: limits)
      guard status == 0,
        let text = String(data: data, encoding: .utf8)?.trimmingCharacters(
          in: .whitespacesAndNewlines), text.count < 120
      else { throw Failure.incompatible }
      let pattern =
        provider == .codex
        ? #"^codex-cli [0-9]+\.[0-9]+\.[0-9]+(?:[-.a-zA-Z0-9]*)$"#
        : #"^[0-9]+\.[0-9]+\.[0-9]+(?:[-.a-zA-Z0-9]*) \(Claude Code\)$"#
      guard text.range(of: pattern, options: .regularExpression) != nil else {
        throw Failure.incompatible
      }
      var activity = resolved
      if provider == .codex && resolved.pathExtension == "js" {
        let root = resolved.deletingLastPathComponent().deletingLastPathComponent()
        #if arch(arm64)
          let arch = "arm64"
          let triple = "aarch64-apple-darwin"
        #else
          let arch = "x64"
          let triple = "x86_64-apple-darwin"
        #endif
        let candidates = [
          root.appendingPathComponent(
            "node_modules/@openai/codex-darwin-\(arch)/vendor/\(triple)/bin/codex"),
          root.appendingPathComponent("vendor/\(triple)/bin/codex"),
        ]
        guard
          let native = candidates.compactMap({ RuntimePaths.discoveryExecutable($0) }).first(
            where: { fm.isExecutableFile(atPath: $0.path) })
        else { throw Failure.incompatible }
        activity = native
      }
      let installation = Installation(
        executable: alias, activityExecutable: activity, version: text)
      cache[provider] = (resolved.path, modified, installation)
      Diagnostics.shared.record("discovered", provider: provider)
      return installation
    }
    cache.removeValue(forKey: provider)
    throw Failure.notInstalled
  }
}
