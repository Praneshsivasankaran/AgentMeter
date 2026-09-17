import Darwin
import Foundation

enum RuntimePaths {
  // The user home itself may be a Git worktree. An isolated OS temporary
  // directory plus a Git ceiling prevents provider startup traversing it.
  static var providerWork: URL {
    FileManager.default.temporaryDirectory.appendingPathComponent(
      "AgentMeter/ProviderWork", isDirectory: true
    ).resolvingSymlinksInPath()
  }
  static func canonicalDirectory(_ url: URL) -> URL {
    guard let path = realpath(url.path, nil) else { return url }
    defer { free(path) }
    return URL(fileURLWithPath: String(cString: path), isDirectory: true)
  }
  // Resolve links one component at a time, rejecting protected targets before
  // stat/access can follow a seemingly safe PATH symlink into a protected folder.
  static func discoveryExecutable(
    _ url: URL, home: String = FileManager.default.homeDirectoryForCurrentUser.path
  ) -> URL? {
    var path = lexical(url.path)
    for _ in 0..<32 {
      guard permitsDiscovery(path, home: home) else { return nil }
      let parts = path.split(separator: "/")
      guard parts.count <= 128 else { return nil }
      var prefix = ""
      var redirected = false
      for (index, part) in parts.enumerated() {
        prefix += "/" + part
        guard permitsDiscovery(prefix, home: home) else { return nil }
        var info = stat()
        guard lstat(prefix, &info) == 0 else { return nil }
        if (info.st_mode & S_IFMT) == S_IFLNK {
          var bytes = [CChar](repeating: 0, count: Int(PATH_MAX) + 1)
          let count = readlink(prefix, &bytes, Int(PATH_MAX))
          guard count > 0, count < Int(PATH_MAX) else { return nil }
          let target = String(
            decoding: bytes.prefix(count).map { UInt8(bitPattern: $0) }, as: UTF8.self)
          let base =
            target.hasPrefix("/")
            ? target
            : URL(fileURLWithPath: prefix).deletingLastPathComponent().appendingPathComponent(
              target
            ).path
          let tail = parts.dropFirst(index + 1).joined(separator: "/")
          path = lexical(base + "/" + tail)
          redirected = true
          break
        }
      }
      if !redirected { return URL(fileURLWithPath: path) }
    }
    return nil
  }
  private static func lexical(_ path: String) -> String {
    var parts: [Substring] = []
    for part in path.split(separator: "/") {
      if part == "." { continue }
      if part == ".." { if !parts.isEmpty { parts.removeLast() } } else { parts.append(part) }
    }
    return "/" + parts.joined(separator: "/")
  }
  static func permitsDiscovery(
    _ path: String, home: String = FileManager.default.homeDirectoryForCurrentUser.path
  ) -> Bool {
    guard path.hasPrefix("/") else { return false }
    let normalized = lexical(path)
    return !["Documents", "Desktop", "Downloads", "Music", "Pictures", "Movies"].contains { name in
      let protected = home + "/" + name
      return normalized == protected || normalized.hasPrefix(protected + "/")
    }
  }
}
