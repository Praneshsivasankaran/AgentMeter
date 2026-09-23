import Foundation

final class Diagnostics: @unchecked Sendable {
  static let shared = Diagnostics()
  private let lock = NSLock()
  private let url: URL
  init(directory: URL? = nil) {
    let dir =
      directory
      ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(
        "Library/Logs/Llumi", isDirectory: true)
    try? FileManager.default.createDirectory(
      at: dir, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
    url = dir.appendingPathComponent("diagnostics.jsonl")
  }
  // Call sites pass fixed categories and numeric counters, never provider payloads or identities.
  func record(
    _ event: String, provider: ProviderID? = nil, code: String? = nil, seconds: Double? = nil,
    value: Int? = nil
  ) {
    var o: [String: Any] = ["time": Date().timeIntervalSince1970, "event": event, "pid": getpid()]
    if let provider { o["provider"] = provider.rawValue }
    if let code { o["code"] = code }
    if let seconds { o["seconds"] = seconds }
    if let value { o["value"] = value }
    guard let data = try? JSONSerialization.data(withJSONObject: o, options: .sortedKeys) else {
      return
    }
    lock.lock()
    defer { lock.unlock() }
    if let a = try? FileManager.default.attributesOfItem(atPath: url.path),
      (a[.size] as? NSNumber)?.intValue ?? 0 > 262144
    {
      let old = url.appendingPathExtension("1")
      try? FileManager.default.removeItem(at: old)
      try? FileManager.default.moveItem(at: url, to: old)
    }
    if !FileManager.default.fileExists(atPath: url.path) {
      FileManager.default.createFile(
        atPath: url.path, contents: nil, attributes: [.posixPermissions: 0o600])
    }
    if let f = try? FileHandle(forWritingTo: url) {
      defer { try? f.close() }
      _ = try? f.seekToEnd()
      try? f.write(contentsOf: data + Data([10]))
    }
  }
}
struct ProcessIdentity: Hashable, Sendable {
  let pid: Int32
  let seconds: UInt64
  let microseconds: UInt64
  static func read(_ pid: Int32) -> Self? {
    var s: UInt64 = 0
    var u: UInt64 = 0
    var p: Int32 = 0
    guard am_identity(pid, &s, &u, &p) == 1 else { return nil }
    return .init(pid: pid, seconds: s, microseconds: u)
  }
}
final class OwnedProcesses: @unchecked Sendable {
  static let shared = OwnedProcesses()
  private let lock = NSLock()
  private var identities: Set<ProcessIdentity> = []
  init() { if let own = ProcessIdentity.read(getpid()) { identities.insert(own) } }
  func add(_ id: ProcessIdentity) {
    lock.lock()
    defer { lock.unlock() }
    identities.insert(id)
  }
  func remove(_ id: ProcessIdentity) {
    lock.lock()
    defer { lock.unlock() }
    identities.remove(id)
  }
  func contains(_ id: ProcessIdentity) -> Bool {
    lock.lock()
    let known = identities
    lock.unlock()
    var current = id
    for _ in 0..<32 {
      if known.contains(current) { return true }
      var s: UInt64 = 0
      var u: UInt64 = 0
      var parent: Int32 = 0
      guard am_identity(current.pid, &s, &u, &parent) == 1, s == current.seconds,
        u == current.microseconds, parent > 1, parent != current.pid,
        let next = ProcessIdentity.read(parent)
      else { return false }
      current = next
    }
    return false
  }
}
