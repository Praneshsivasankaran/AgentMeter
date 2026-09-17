import Foundation

// One-shot kernel readiness/exit/deadline wait. All races converge on one locked
// continuation; providers never require a 10 ms polling loop in a background app.
final class IOAwaiter: @unchecked Sendable {
  private let lock = NSLock()
  private var completed = false
  private var continuation: CheckedContinuation<Void, Never>?
  private var sources: [any DispatchSourceProtocol] = []
  init(readFDs: [Int32] = [], writeFD: Int32? = nil, pid: Int32, deadline: ContinuousClock.Instant)
  {
    let queue = DispatchQueue.global(qos: .utility)
    for fd in readFDs where fd >= 0 {
      let source = DispatchSource.makeReadSource(fileDescriptor: fd, queue: queue)
      source.setEventHandler { [weak self] in self?.finish() }
      sources.append(source)
    }
    if let fd = writeFD, fd >= 0 {
      let source = DispatchSource.makeWriteSource(fileDescriptor: fd, queue: queue)
      source.setEventHandler { [weak self] in self?.finish() }
      sources.append(source)
    }
    let exit = DispatchSource.makeProcessSource(identifier: pid, eventMask: .exit, queue: queue)
    exit.setEventHandler { [weak self] in self?.finish() }
    sources.append(exit)
    let timer = DispatchSource.makeTimerSource(queue: queue)
    let remaining = max(0, ProviderAdapter.seconds(ContinuousClock.now.duration(to: deadline)))
    timer.schedule(deadline: .now() + remaining)
    timer.setEventHandler { [weak self] in self?.finish() }
    sources.append(timer)
  }
  func wait() async {
    await withTaskCancellationHandler {
      await withCheckedContinuation { c in
        lock.lock()
        if completed {
          lock.unlock()
          c.resume()
          return
        }
        continuation = c
        let start = sources
        lock.unlock()
        start.forEach { $0.activate() }
      }
    } onCancel: {
      self.finish()
    }
  }
  private func finish() {
    lock.lock()
    guard !completed else {
      lock.unlock()
      return
    }
    completed = true
    let c = continuation
    continuation = nil
    let cancel = sources
    sources.removeAll()
    lock.unlock()
    // activate() is idempotent and balances cancellation-before-activation.
    cancel.forEach {
      $0.cancel()
      $0.activate()
    }
    c?.resume()
  }
}
