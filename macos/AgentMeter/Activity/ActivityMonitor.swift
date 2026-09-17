import AppKit
import CoreGraphics

// Mutable scanner state is confined to one utility queue; no UI work runs there.
final class ProcessScanner: @unchecked Sendable {
  private let queue = DispatchQueue(label: "AgentMeter.activity", qos: .utility)
  private var timer: DispatchSourceTimer?
  private var running = false
  private var installations: [ProviderID: Installation] = [:]
  private var active: [ProcessIdentity: ProviderID] = [:]
  private var watchers: [ProcessIdentity: DispatchSourceProcess] = [:]
  private var revision = 0
  private var cpuSeconds = 0.0
  private let started = ContinuousClock.now
  private let deliver: @Sendable (Int, Set<ProviderID>) -> Void
  private let tick: @Sendable () -> Void
  init(
    deliver: @escaping @Sendable (Int, Set<ProviderID>) -> Void,
    tick: @escaping @Sendable () -> Void
  ) {
    self.deliver = deliver
    self.tick = tick
  }
  func start() {
    queue.async { [self] in
      guard timer == nil else { return }
      running = true
      let t = DispatchSource.makeTimerSource(queue: queue)
      timer = t
      t.schedule(deadline: .now(), repeating: .milliseconds(500), leeway: .milliseconds(10))
      t.setEventHandler { [weak self] in
        self?.scan()
        self?.tick()
      }
      t.resume()
    }
  }
  func update(_ p: ProviderID, _ install: Installation) {
    queue.async { [self] in
      guard running else { return }
      installations[p] = install
      am_set_alias(p == .codex ? 1 : 2, install.executable.path)
      scan()
    }
  }
  func reconcile() {
    queue.async { [weak self] in
      self?.scan()
      self?.tick()
    }
  }
  func stop() {
    queue.sync {
      running = false
      timer?.cancel()
      timer = nil
      watchers.values.forEach { $0.cancel() }
      watchers.removeAll()
      active.removeAll()
      Diagnostics.shared.record(
        "activity-cost", seconds: cpuSeconds,
        value: Int(ProviderAdapter.seconds(started.duration(to: .now)) * 1000))
    }
  }
  private func publish(_ previous: Set<ProviderID>) {
    let current = Set(active.values)
    if previous != current {
      revision += 1
      deliver(revision, current)
    }
  }
  private func scan() {
    guard running else { return }
    var before = timespec()
    clock_gettime(CLOCK_THREAD_CPUTIME_ID, &before)
    defer {
      var after = timespec()
      clock_gettime(CLOCK_THREAD_CPUTIME_ID, &after)
      cpuSeconds +=
        Double(after.tv_sec - before.tv_sec) + Double(after.tv_nsec - before.tv_nsec) / 1e9
    }
    let previous = Set(active.values)
    var found: [ProcessIdentity: ProviderID] = [:]
    var rows = [Candidate](repeating: Candidate(), count: 256)
    let count = am_scan(
      &rows, 256, installations[.codex]?.activityExecutable.path ?? "",
      installations[.claude]?.activityExecutable.path ?? "")
    var seen = Set<ProcessIdentity>()
    for c in rows.prefix(max(0, Int(count))) {
      let id = ProcessIdentity(pid: c.pid, seconds: c.sec, microseconds: c.usec)
      seen.insert(id)
      let p: ProviderID = c.provider == 1 ? .codex : .claude
      if c.tty != 0 && !OwnedProcesses.shared.contains(id) && am_mode(c.pid, c.provider) == 1
        && ProcessIdentity.read(c.pid) == id
      {
        found[id] = p
      }
      if watchers[id] == nil {
        let source = DispatchSource.makeProcessSource(
          identifier: c.pid, eventMask: [.exit, .exec], queue: queue)
        watchers[id] = source
        source.setEventHandler { [weak self, weak source] in
          guard let self, let source else { return }
          if source.data.contains(.exit) {
            let previous = Set(self.active.values)
            self.watchers.removeValue(forKey: id)?.cancel()
            self.active.removeValue(forKey: id)
            am_forget(id.pid)
            self.publish(previous)
          } else {
            am_forget(id.pid)
            self.scan()
          }
        }
        source.resume()
      }
    }
    for id in Array(watchers.keys) where !seen.contains(id) && ProcessIdentity.read(id.pid) != id {
      watchers.removeValue(forKey: id)?.cancel()
      am_forget(id.pid)
    }
    active = found
    publish(previous)
  }
}
@MainActor final class ActivityMonitor {
  private var state = ActivitySnapshot()
  private var revision = 0
  private var observers: [NSObjectProtocol] = []
  private let changed: (ActivitySnapshot) -> Void
  private var scanner: ProcessScanner!
  static let bundles: [String: ProviderID] = [
    "com.openai.codex": .codex, "com.anthropic.claudefordesktop": .claude,
  ]
  init(changed: @escaping (ActivitySnapshot) -> Void) {
    self.changed = changed
    scanner = ProcessScanner(
      deliver: { [weak self] r, providers in
        Task { @MainActor in
          guard let self, r > self.revision else { return }
          self.revision = r
          self.state.codex.cli = providers.contains(.codex)
          self.state.claude.cli = providers.contains(.claude)
          self.emit()
        }
      }, tick: { [weak self] in Task { @MainActor in self?.desktop() } })
  }
  func start() {
    for (bundle, p) in Self.bundles {
      if let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundle),
        Bundle(url: url)?.bundleIdentifier == bundle
      {
        Diagnostics.shared.record("desktop-discovered", provider: p)
      }
    }
    for n in [
      NSWorkspace.didActivateApplicationNotification,
      NSWorkspace.didDeactivateApplicationNotification, NSWorkspace.didHideApplicationNotification,
      NSWorkspace.didUnhideApplicationNotification, NSWorkspace.didTerminateApplicationNotification,
    ] {
      observers.append(
        NSWorkspace.shared.notificationCenter.addObserver(forName: n, object: nil, queue: .main) {
          [weak self] _ in MainActor.assumeIsolated { self?.desktop() }
        })
    }
    desktop()
    scanner.start()
  }
  func update(_ p: ProviderID, _ installation: Installation) { scanner.update(p, installation) }
  func reconcile() {
    scanner.reconcile()
    desktop()
  }
  func stop() {
    scanner.stop()
    observers.forEach { NSWorkspace.shared.notificationCenter.removeObserver($0) }
    observers.removeAll()
  }
  static func visibleWindowCount(_ pid: Int32) -> Int {
    guard
      let rows = CGWindowListCopyWindowInfo(
        [.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]]
    else { return 0 }
    return rows.reduce(0) { count, row in
      guard (row[kCGWindowOwnerPID as String] as? Int) == Int(pid),
        (row[kCGWindowLayer as String] as? Int) == 0,
        (row[kCGWindowAlpha as String] as? Double ?? 1) > 0
      else { return count }
      return count + 1
    }
  }
  private func desktop() {
    let old = state
    let app = NSWorkspace.shared.frontmostApplication
    let provider = Self.bundles[app?.bundleIdentifier ?? ""]
    let visible =
      provider != nil
      && ActivitySnapshot.desktopActive(
        frontmost: true, hidden: app?.isHidden ?? true,
        normalWindows: Self.visibleWindowCount(app?.processIdentifier ?? 0))
    state.codex.desktop = visible && provider == .codex
    state.claude.desktop = visible && provider == .claude
    if old != state { emit() }
  }
  private func emit() { changed(state) }
}
