import AppKit
import Observation
import ServiceManagement

enum AppAppearance: String, CaseIterable, Identifiable {
  case system, light, dark
  var id: String { rawValue }
  var title: String { rawValue.capitalized }
  var native: NSAppearance? {
    switch self {
    case .system: nil
    case .light: NSAppearance(named: .aqua)
    case .dark: NSAppearance(named: .darkAqua)
    }
  }
}
@MainActor @Observable final class Preferences {
  private let defaults: UserDefaults
  var changed: () -> Void = {}
  var notchEnabled: Bool {
    didSet {
      defaults.set(notchEnabled, forKey: "notchEnabled")
      changed()
    }
  }
  var menuEnabled: Bool {
    didSet {
      defaults.set(menuEnabled, forKey: "menuEnabled")
      changed()
    }
  }
  var appearance: AppAppearance {
    didSet {
      defaults.set(appearance.rawValue, forKey: "appearance")
      changed()
    }
  }
  init(defaults: UserDefaults = .standard) {
    self.defaults = defaults
    notchEnabled = defaults.object(forKey: "notchEnabled") as? Bool ?? true
    menuEnabled = defaults.object(forKey: "menuEnabled") as? Bool ?? true
    appearance = AppAppearance(rawValue: defaults.string(forKey: "appearance") ?? "") ?? .system
  }
}
@MainActor @Observable final class LoginItem {
  private(set) var enabled = false
  private(set) var message: String?
  init() { synchronize() }
  func synchronize() {
    let status = SMAppService.mainApp.status
    enabled = [.enabled, .requiresApproval].contains(status)
    message =
      status == .requiresApproval ? "Approval is needed in System Settings → Login Items." : nil
  }
  func setEnabled(_ value: Bool) {
    var failed = false
    do {
      if value {
        try SMAppService.mainApp.register()
      } else {
        try SMAppService.mainApp.unregister()
      }
    } catch {
      failed = true
      Diagnostics.shared.record(
        "login-item-error", code: (error as NSError).domain, value: (error as NSError).code)
    }
    synchronize()
    if failed {
      message = "macOS couldn’t change the login item. Try again from the installed app."
    }
  }
}
