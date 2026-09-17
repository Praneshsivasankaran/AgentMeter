import Foundation
import Observation

enum NotchPhase: String { case hidden, compact, expanded }
struct NotchState: Equatable {
  private(set) var phase: NotchPhase = .hidden
  private(set) var providers: [ProviderID] = []
  mutating func reconcile(activity: ActivitySnapshot, enabled: Bool) {
    providers = enabled ? activity.providers : []
    if providers.isEmpty { phase = .hidden } else if phase == .hidden { phase = .compact }
  }
  mutating func hover(_ inside: Bool) {
    guard !providers.isEmpty else {
      phase = .hidden
      return
    }
    phase = inside ? .expanded : .compact
  }
}
struct ProviderGlance: Identifiable {
  let snapshot: UsageSnapshot
  var id: ProviderID { snapshot.provider }
  var percentage: String {
    snapshot.primary?.remaining.map(UsageSnapshot.percent)
      ?? (snapshot.state == .loading ? "…" : "--")
  }
  var accessibility: String {
    "\(id.title), \(snapshot.primary?.remaining.map { UsageSnapshot.percent($0)+" remaining" } ?? snapshot.state.rawValue), \(snapshot.state.rawValue). \(snapshot.primary?.resetText(at: Date()) ?? "")"
  }
}
@MainActor @Observable final class NotchPresentation {
  var state = NotchState()
  var rows: [ProviderGlance] = []
}
