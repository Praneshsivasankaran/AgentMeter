import Darwin
import Foundation

struct ProcessLimits: Sendable {
  var seconds: Double = 18
  var stdout = 1_048_576
  var stderr = 65536
  var line = 262144
  var lines = 2048
}
// A session is actor-confined. The unreaped leader pins its PID until group cleanup.
actor Subprocess {
  let pid: Int32
  private let identity: ProcessIdentity?
  private var input: Int32
  private var output: Int32
  private var error: Int32
  private let deadline: ContinuousClock.Instant
  private let limits: ProcessLimits
  private var buffer = Data()
  private var stdoutCount = 0
  private var stderrCount = 0
  private var lineCount = 0
  private var errorEnded = false
  private var ended = false
  private var closed = false
  private(set) var peakRSS: UInt64 = 0
  private(set) var peakGroupRSS: UInt64 = 0
  private var sampled = ContinuousClock.now.advanced(by: .seconds(-1))
  private var receivedLines = 0
  private var errorLines = 0
  init(
    executable: URL, arguments: [String], environment: [String] = [], directory: URL,
    limits: ProcessLimits = .init()
  ) throws {
    self.limits = limits
    deadline = .now.advanced(by: .seconds(limits.seconds))
    var input: Int32 = -1
    var output: Int32 = -1
    var error: Int32 = -1
    let strings = ([executable.path] + arguments).map { strdup($0) }
    let env = environment.map { strdup($0) }
    defer {
      strings.forEach { free($0) }
      env.forEach { free($0) }
    }
    let child = (strings + [nil]).withUnsafeBufferPointer { argv in
      (env + [nil]).withUnsafeBufferPointer { envp in
        am_spawn(
          executable.path, argv.baseAddress, envp.baseAddress, directory.path, &input, &output,
          &error)
      }
    }
    guard child > 0 else { throw Failure.processExited }
    pid = child
    self.input = input
    self.output = output
    self.error = error
    identity = ProcessIdentity.read(child)
    if let identity { OwnedProcesses.shared.add(identity) }
    Diagnostics.shared.record("child-start", value: Int(child))
  }
  private func check() throws {
    try Task.checkCancellation()
    if closed { throw Failure.cancelled }
    if ContinuousClock.now >= deadline { throw Failure.timeout }
  }
  func send(_ value: J) async throws {
    let data = try value.encoded() + Data([10])
    guard data.count <= limits.line else { throw Failure.outputLimit }
    var offset = 0
    while offset < data.count {
      try check()
      let n = data.withUnsafeBytes {
        write(input, $0.baseAddress!.advanced(by: offset), data.count - offset)
      }
      if n > 0 {
        offset += n
      } else if errno != EAGAIN && errno != EINTR {
        throw Failure.processExited
      } else {
        await IOAwaiter(writeFD: input, pid: pid, deadline: deadline).wait()
      }
    }
  }
  private func pump() throws {
    if sampled.duration(to: .now) >= .milliseconds(250) {
      sampled = .now
      peakRSS = max(peakRSS, am_rss(pid))
      peakGroupRSS = max(peakGroupRSS, am_group_rss(pid))
    }
    var bytes = [UInt8](repeating: 0, count: 8192)
    for fd in [output, error] {
      for _ in 0..<16 {
        let n = read(fd, &bytes, bytes.count)
        if n == 0 {
          if fd == output { ended = true } else { errorEnded = true }
          break
        }
        if n < 0 {
          if errno == EAGAIN || errno == EINTR { break }
          throw Failure.processExited
        }
        if fd == output {
          stdoutCount += n
          receivedLines += bytes.prefix(n).filter { $0 == 10 }.count
          guard receivedLines <= limits.lines else { throw Failure.outputLimit }
          guard stdoutCount <= limits.stdout else { throw Failure.outputLimit }
          buffer.append(contentsOf: bytes.prefix(n))
        } else {
          stderrCount += n
          errorLines += bytes.prefix(n).filter { $0 == 10 }.count
          guard errorLines <= limits.lines else { throw Failure.outputLimit }
          guard stderrCount <= limits.stderr else { throw Failure.outputLimit }
        }
      }
    }
    if buffer.count > limits.line && !buffer.contains(10) { throw Failure.outputLimit }
  }
  func next() async throws -> J {
    while true {
      try check()
      try pump()
      if let end = buffer.firstIndex(of: 10) {
        let data = Data(buffer[..<end])
        buffer.removeSubrange(...end)
        lineCount += 1
        guard data.count <= limits.line && lineCount <= limits.lines else {
          throw Failure.outputLimit
        }
        if data.isEmpty { continue }
        return try J.parse(data)
      }
      if ended { throw Failure.processExited }
      await IOAwaiter(
        readFDs: ([ended ? -1 : output, errorEnded ? -1 : error]), pid: pid, deadline: deadline
      ).wait()
    }
  }
  func finite() async throws -> Data {
    if input >= 0 {
      Darwin.close(input)
      input = -1
    }
    while !ended {
      try check()
      try pump()
      if !ended {
        await IOAwaiter(
          readFDs: ([ended ? -1 : output, errorEnded ? -1 : error]), pid: pid, deadline: deadline
        ).wait()
      }
    }
    while am_has_exited(pid) == 0 {
      try check()
      try pump()
      await IOAwaiter(readFDs: ([errorEnded ? -1 : error]), pid: pid, deadline: deadline).wait()
    }
    guard buffer.count <= limits.line else { throw Failure.outputLimit }
    return buffer
  }
  // Cancellation must not skip cleanup: the brief grace is intentionally uncancellable.
  @discardableResult func close() async -> Int32 {
    if closed { return -1 }
    closed = true
    if input >= 0 {
      Darwin.close(input)
      input = -1
    }
    am_group_signal(pid, SIGTERM)
    await withCheckedContinuation { continuation in
      DispatchQueue.global().asyncAfter(deadline: .now() + 0.1) { continuation.resume() }
    }
    am_group_signal(pid, SIGKILL)
    let status = am_reap(pid)
    Darwin.close(output)
    Darwin.close(error)
    output = -1
    error = -1
    if let identity { OwnedProcesses.shared.remove(identity) }
    Diagnostics.shared.record("child-exit", code: String(status), value: Int(pid))
    return status
  }
  static func run(
    executable: URL, arguments: [String], environment: [String], directory: URL,
    limits: ProcessLimits = .init()
  ) async throws -> (Data, Int32) {
    let p = try Subprocess(
      executable: executable, arguments: arguments, environment: environment, directory: directory,
      limits: limits)
    do {
      let data = try await p.finite()
      let status = await p.close()
      return (data, status)
    } catch {
      await p.close()
      throw error
    }
  }
}
