import Darwin
import Foundation

// Persistent empty inode, not a PID file. Never unlink: that would split the lock
// across inodes. O_CLOEXEC prevents provider children from retaining ownership.
final class InstanceLease {
  static let identifier = "io.github.praneshsivasankaran.llumi"
  static let legacyIdentifier = "io.github.praneshsivasankaran.agentmeter"
  static let reopen = Notification.Name(identifier + ".reopen")
  private let descriptor: Int32
  private init(_ descriptor: Int32) { self.descriptor = descriptor }
  deinit { close(descriptor) }
  enum LeaseError: Error { case unsafeLocation, unavailable }
  static func acquireProductLeases(root: URL? = nil) throws -> [InstanceLease]? {
    let root = root ?? FileManager.default.homeDirectoryForCurrentUser
      .appendingPathComponent("Library/Application Support", isDirectory: true)
    var leases: [InstanceLease] = []
    for id in [identifier, legacyIdentifier] {
      guard let lease = try acquire(directory: root.appendingPathComponent(id, isDirectory: true)) else { return nil }
      leases.append(lease)
    }
    return leases
  }
  static func acquire(directory: URL? = nil) throws -> InstanceLease? {
    let directory = directory ?? FileManager.default.homeDirectoryForCurrentUser
      .appendingPathComponent("Library/Application Support/" + identifier, isDirectory: true)
    try FileManager.default.createDirectory(
      at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
    let dir = open(directory.path, O_RDONLY | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC)
    guard dir >= 0 else { throw LeaseError.unsafeLocation }
    defer { close(dir) }
    var info = stat()
    guard fstat(dir, &info) == 0, info.st_uid == getuid(), info.st_mode & 0o077 == 0 else {
      throw LeaseError.unsafeLocation
    }
    let fd = openat(dir, "instance.lock", O_RDWR | O_CREAT | O_NOFOLLOW | O_CLOEXEC, 0o600)
    guard fd >= 0 else { throw LeaseError.unavailable }
    guard fstat(fd, &info) == 0, info.st_uid == getuid(), info.st_mode & S_IFMT == S_IFREG,
      info.st_mode & 0o077 == 0, info.st_nlink == 1 else {
      close(fd)
      throw LeaseError.unsafeLocation
    }
    guard flock(fd, LOCK_EX | LOCK_NB) == 0 else {
      let code = errno
      close(fd)
      if code == EWOULDBLOCK { return nil }
      throw LeaseError.unavailable
    }
    return InstanceLease(fd)
  }
}

enum RuntimePaths {
  // The user home itself may be a Git worktree. An isolated OS temporary
  // directory plus a Git ceiling prevents provider startup traversing it.
  static var providerWork: URL {
    FileManager.default.temporaryDirectory.appendingPathComponent(
      "Llumi/ProviderWork", isDirectory: true
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
