import AppKit
import Foundation
import XCTest
import Darwin

@MainActor final class MigrationTests: XCTestCase {
  private func suite(_ body: (UserDefaults) throws -> Void) rethrows {
    let name = "AgentMeter-migration-" + UUID().uuidString
    let defaults = UserDefaults(suiteName: name)!
    defer { defaults.removePersistentDomain(forName: name) }
    try body(defaults)
  }
  func testAllowlistOnlyAndNoLoginRegistrationCopy() {
    suite { d in
      BetaPreferences.migrate(from: ["notchEnabled": false, "menuEnabled": true,
        "appearance": "dark", "loginEnabled": true, "token": "synthetic", "usage": 9], to: d)
      XCTAssertEqual(d.object(forKey: "notchEnabled") as? Bool, false)
      XCTAssertEqual(d.object(forKey: "menuEnabled") as? Bool, true)
      XCTAssertEqual(d.string(forKey: "appearance"), "dark")
      for key in ["loginEnabled", "token", "usage"] { XCTAssertNil(d.object(forKey: key)) }
      XCTAssertTrue(d.bool(forKey: BetaPreferences.completion))
    }
  }
  func testMalformedValuesUseDefaults() {
    suite { d in
      BetaPreferences.migrate(from: ["notchEnabled": 1, "menuEnabled": "false",
        "appearance": "unknown"], to: d)
      let p = Preferences(defaults: d)
      XCTAssertTrue(p.notchEnabled); XCTAssertTrue(p.menuEnabled)
      XCTAssertEqual(p.appearance, .system)
    }
  }
  func testProductionValuesWinAndMigrationRunsOnce() {
    suite { d in
      d.set("light", forKey: "appearance")
      BetaPreferences.migrate(from: ["appearance": "dark", "menuEnabled": false], to: d)
      XCTAssertEqual(d.string(forKey: "appearance"), "light")
      d.set(true, forKey: "menuEnabled")
      BetaPreferences.migrate(from: ["menuEnabled": false, "notchEnabled": false], to: d)
      XCTAssertTrue(d.bool(forKey: "menuEnabled"))
      XCTAssertNil(d.object(forKey: "notchEnabled"))
    }
  }
  func testAllAppearancesAndMissingBeta() {
    for value in ["system", "light", "dark"] {
      suite { d in
        BetaPreferences.migrate(from: ["appearance": value], to: d)
        XCTAssertEqual(Preferences(defaults: d).appearance.rawValue, value)
      }
    }
    suite { d in
      BetaPreferences.migrate(from: [:], to: d)
      XCTAssertTrue(d.bool(forKey: BetaPreferences.completion))
    }
  }
}

@MainActor final class SingleInstanceTests: XCTestCase {
  private func directory() throws -> URL {
    let d = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
    try FileManager.default.createDirectory(at: d, withIntermediateDirectories: true,
      attributes: [.posixPermissions: 0o700])
    return d
  }
  func testExclusiveUntilReleaseAndPersistentInodeReacquired() throws {
    let d = try directory(); defer { try? FileManager.default.removeItem(at: d) }
    var first = try InstanceLease.acquire(directory: d)
    XCTAssertNotNil(first)
    for _ in 0..<20 { XCTAssertNil(try InstanceLease.acquire(directory: d)) }
    first = nil
    XCTAssertNotNil(try InstanceLease.acquire(directory: d))
    XCTAssertTrue(FileManager.default.fileExists(atPath: d.appendingPathComponent("instance.lock").path))
  }
  func testSymlinkAndHardLinkRejectedWithoutModifyingTarget() throws {
    let d = try directory(); defer { try? FileManager.default.removeItem(at: d) }
    let target = d.appendingPathComponent("target")
    try Data("unchanged".utf8).write(to: target)
    let lock = d.appendingPathComponent("instance.lock")
    try FileManager.default.createSymbolicLink(at: lock, withDestinationURL: target)
    XCTAssertThrowsError(try InstanceLease.acquire(directory: d))
    try FileManager.default.removeItem(at: lock)
    XCTAssertEqual(link(target.path, lock.path), 0)
    XCTAssertThrowsError(try InstanceLease.acquire(directory: d))
    XCTAssertEqual(try String(contentsOf: target, encoding: .utf8), "unchanged")
  }
  func testUnsafeDirectoryAndFilePermissionsFailClosed() throws {
    let d = try directory(); defer { try? FileManager.default.removeItem(at: d) }
    XCTAssertEqual(chmod(d.path, 0o777), 0)
    XCTAssertThrowsError(try InstanceLease.acquire(directory: d))
    XCTAssertEqual(chmod(d.path, 0o700), 0)
    let lease = try InstanceLease.acquire(directory: d)
    XCTAssertNotNil(lease)
    XCTAssertEqual(chmod(d.appendingPathComponent("instance.lock").path, 0o666), 0)
    XCTAssertThrowsError(try InstanceLease.acquire(directory: d))
    withExtendedLifetime(lease) {}
  }
}

@MainActor final class Phase2Tests: XCTestCase {
  func testNotchAcceptsFirstClickWhileAppIsInBackground() {
    let surface = TrackingSurface()
    XCTAssertTrue(surface.acceptsFirstMouse(for: nil))
    var opened = 0
    surface.clicked = { opened += 1 }
    XCTAssertTrue(surface.accessibilityPerformPress())
    XCTAssertEqual(opened, 1)
  }
  private func activity(_ codex: Bool, _ claude: Bool) -> ActivitySnapshot {
    .init(codex: .init(cli: codex), claude: .init(cli: claude))
  }
  func testForwardAndReverseActivityTransitions() {
    for order in [
      [(false, false), (true, false), (true, true), (false, true), (false, false)],
      [(false, false), (false, true), (true, true), (true, false), (false, false)],
    ] {
      var state = NotchState()
      for (c, a) in order {
        state.reconcile(activity: activity(c, a), enabled: true)
        XCTAssertEqual(state.providers, activity(c, a).providers)
        XCTAssertEqual(state.phase, c || a ? .compact : .hidden)
      }
    }
  }
  func testHoverRetainsExpansionWhenProvidersChange() {
    var state = NotchState()
    state.reconcile(activity: activity(true, false), enabled: true)
    state.hover(true)
    XCTAssertEqual(state.phase, .expanded)
    state.reconcile(activity: activity(true, true), enabled: true)
    XCTAssertEqual(state.phase, .expanded)
    XCTAssertEqual(state.providers.count, 2)
    state.reconcile(activity: activity(false, true), enabled: true)
    XCTAssertEqual(state.phase, .expanded)
    XCTAssertEqual(state.providers, [.claude])
    state.hover(false)
    XCTAssertEqual(state.phase, .compact)
  }
  func testEndingActivityWhileHoveredHidesCompletely() {
    var state = NotchState()
    state.reconcile(activity: activity(true, true), enabled: true)
    state.hover(true)
    state.reconcile(activity: activity(false, false), enabled: true)
    state.hover(true)
    XCTAssertEqual(state.phase, .hidden)
    XCTAssertTrue(state.providers.isEmpty)
  }
  func testDisablingNotchDoesNotChangeActivityAndReenableReflectsTruth() {
    let active = activity(true, true)
    var state = NotchState()
    state.reconcile(activity: active, enabled: true)
    state.hover(true)
    state.reconcile(activity: active, enabled: false)
    XCTAssertEqual(state.phase, .hidden)
    XCTAssertTrue(state.providers.isEmpty)
    XCTAssertEqual(active.providers.count, 2)
    state.reconcile(activity: active, enabled: true)
    XCTAssertEqual(state.phase, .compact)
    XCTAssertEqual(state.providers.count, 2)
  }
  func testSurfaceDeduplicationInNotch() {
    var state = NotchState()
    state.reconcile(
      activity: .init(
        codex: .init(cli: true, desktop: true), claude: .init(cli: true, desktop: true)),
      enabled: true)
    XCTAssertEqual(state.providers, [.codex, .claude])
  }
  func testPreferencesDefaultAndPersistenceWithoutUsageOrActivity() {
    let name = "AgentMeter-tests-\(UUID().uuidString)"
    let defaults = UserDefaults(suiteName: name)!
    defer { defaults.removePersistentDomain(forName: name) }
    let prefs = Preferences(defaults: defaults)
    XCTAssertTrue(prefs.notchEnabled)
    XCTAssertTrue(prefs.menuEnabled)
    XCTAssertEqual(prefs.appearance, .system)
    var changes = 0
    prefs.changed = { changes += 1 }
    prefs.notchEnabled = false
    prefs.menuEnabled = false
    prefs.appearance = .dark
    let restored = Preferences(defaults: defaults)
    XCTAssertFalse(restored.notchEnabled)
    XCTAssertFalse(restored.menuEnabled)
    XCTAssertEqual(restored.appearance, .dark)
    XCTAssertEqual(changes, 3)
    XCTAssertEqual(defaults.persistentDomain(forName: name)?.count, 3)
  }
  func testAppearanceSystemAndOverrides() {
    XCTAssertNil(AppAppearance.system.native)
    XCTAssertEqual(AppAppearance.light.native?.name.rawValue, "NSAppearanceNameAqua")
    XCTAssertEqual(AppAppearance.dark.native?.name.rawValue, "NSAppearanceNameDarkAqua")
  }
  func testLoadingAndUnknownGlancesNeverInventQuota() {
    var snapshot = UsageSnapshot(provider: .codex)
    XCTAssertEqual(ProviderGlance(snapshot: snapshot).percentage, "…")
    snapshot.apply(.fail(.notInstalled))
    XCTAssertEqual(ProviderGlance(snapshot: snapshot).percentage, "--")
    XCTAssertTrue(ProviderGlance(snapshot: snapshot).accessibility.contains("Not installed"))
  }
  func testPrimaryCoreSelectionAndStaleGlance() throws {
    var snapshot = UsageSnapshot(provider: .codex)
    let core = try UsageWindow(
      id: "core", bucket: "codex", label: "Main", durationMinutes: 10080, used: 5,
      reset: Date(timeIntervalSince1970: 2_000_000_000))
    let spark = try UsageWindow(
      id: "spark", bucket: "spark", label: "Spark", durationMinutes: 10080, used: 0, reset: nil)
    snapshot.apply(.success(.init(binding: "synthetic", windows: [spark, core], date: Date())))
    XCTAssertEqual(ProviderGlance(snapshot: snapshot).percentage, "95%")
    snapshot.apply(.fail(.timeout, binding: "synthetic"))
    XCTAssertEqual(snapshot.state, .stale)
    XCTAssertEqual(ProviderGlance(snapshot: snapshot).percentage, "95%")
    snapshot.apply(.fail(.accountChanged))
    XCTAssertEqual(ProviderGlance(snapshot: snapshot).percentage, "--")
  }
  func testClaudeLongTermAndMixedUnknownGlances() throws {
    var snapshot = UsageSnapshot(provider: .claude)
    let short = try UsageWindow(
      id: "five_hour", bucket: "claude", label: "", durationMinutes: 300, used: 0, reset: nil)
    let long = try UsageWindow(
      id: "seven_day", bucket: "claude", label: "", durationMinutes: 10080, used: 12, reset: nil)
    snapshot.apply(.success(.init(binding: "synthetic", windows: [short, long], date: Date())))
    let rows = [
      ProviderGlance(snapshot: UsageSnapshot(provider: .codex)), ProviderGlance(snapshot: snapshot),
    ]
    XCTAssertEqual(rows.map(\.percentage), ["…", "88%"])
    XCTAssertTrue(rows[1].accessibility.contains("Claude, 88% remaining"))
  }
}
