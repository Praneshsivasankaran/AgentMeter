import Foundation

actor UsageStore {
  private let sources: [ProviderID: any UsageSource]
  private var flights: [ProviderID: Task<Void, Never>] = [:]
  private var states = Dictionary(
    uniqueKeysWithValues: ProviderID.allCases.map { ($0, UsageSnapshot(provider: $0)) })
  private var suspended = false
  private var stopped = false
  private let timeout: Double
  private let publish: @Sendable ([ProviderID: UsageSnapshot]) -> Void
  init(
    sources: [ProviderID: any UsageSource], timeout: Double = 20,
    publish: @escaping @Sendable ([ProviderID: UsageSnapshot]) -> Void
  ) {
    self.sources = sources
    self.publish = publish
    self.timeout = timeout
  }
  func refresh(_ provider: ProviderID? = nil, onlyIfOlderThan age: Double? = nil) {
    guard !stopped && !suspended else { return }
    for p in provider.map({ [$0] }) ?? ProviderID.allCases {
      guard flights[p] == nil, let source = sources[p] else { continue }
      if let age, let reading = states[p]?.reading, states[p]?.state == .live,
        Date().timeIntervalSince(reading.date) < age
      {
        continue
      }
      let timeout = self.timeout
      flights[p] = Task {
        let result = await withTaskGroup(of: QueryResult.self, returning: QueryResult.self) {
          group in
          group.addTask { await source.query() }
          group.addTask {
            do {
              try await Task.sleep(for: .seconds(timeout))
              return .fail(.timeout)
            } catch { return .fail(.cancelled) }
          }
          let result = await group.next() ?? .fail(.cancelled)
          group.cancelAll()
          return result
        }
        complete(p, result: result)
      }
    }
  }
  private func complete(_ p: ProviderID, result: QueryResult) {
    flights.removeValue(forKey: p)
    guard !stopped && !suspended && result.failure != .cancelled else { return }
    states[p]?.apply(result)
    publish(states)
  }
  func snapshot() -> [ProviderID: UsageSnapshot] { states }
  func waitForIdle() async {
    let work = Array(flights.values)
    for task in work { await task.value }
  }
  func suspend() async {
    suspended = true
    let work = Array(flights.values)
    for t in work { t.cancel() }
    for t in work { await t.value }
  }
  func resume() {
    guard !stopped else { return }
    suspended = false
    refresh()
  }
  func stop() async {
    stopped = true
    await suspend()
  }
}
