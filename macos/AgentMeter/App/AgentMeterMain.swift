import AppKit
import Darwin

@main struct AgentMeterMain {
  @MainActor static func main() {
    signal(SIGPIPE, SIG_IGN)
    let work = RuntimePaths.providerWork
    try? FileManager.default.createDirectory(
      at: work, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
    // Never retain a Finder/development working directory for incidental framework work.
    _ = FileManager.default.changeCurrentDirectoryPath(work.path)
    let app = NSApplication.shared
    app.setActivationPolicy(.regular)
    let delegate = AppDelegate()
    app.delegate = delegate
    withExtendedLifetime(delegate) { app.run() }
  }
}
