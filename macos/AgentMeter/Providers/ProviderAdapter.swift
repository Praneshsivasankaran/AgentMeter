import Foundation

protocol UsageSource: Sendable { func query() async -> QueryResult }
struct ProviderAdapter: UsageSource {
  private var claudeEnvironment: [String] {
    discovery.childEnvironment + ["DISABLE_TELEMETRY=1"]
  }
  let provider: ProviderID
  let discovery: ProviderDiscovery
  var discovered: @Sendable (ProviderID, Installation) -> Void = { _, _ in }
  func query() async -> QueryResult {
    let start = ContinuousClock.now
    do {
      let install = try await discovery.find(provider)
      discovered(provider, install)
      let result = provider == .codex ? await codex(install) : await claude(install)
      Diagnostics.shared.record(
        "refresh", provider: provider, code: result.failure?.rawValue ?? "success",
        seconds: Self.seconds(start.duration(to: .now)))
      return result
    } catch {
      let f = Self.failure(error)
      Diagnostics.shared.record("refresh", provider: provider, code: f.rawValue)
      return .fail(f)
    }
  }
  static func seconds(_ d: Duration) -> Double {
    Double(d.components.seconds) + Double(d.components.attoseconds) / 1e18
  }
  static func failure(_ error: Error) -> Failure {
    error is CancellationError ? .cancelled : (error as? Failure ?? .malformed)
  }
  private func rpc(_ p: Subprocess, id: Int, method: String, params: J = .object([:])) async throws
    -> J
  {
    try await p.send(
      .object(["id": .number(Double(id)), "method": .string(method), "params": params]))
    while true {
      let r = try await p.next()
      if r["id"].number == Double(id) {
        if r["error"] != .null { throw Failure.unavailable }
        guard r.object?.keys.contains("result") == true else { throw Failure.malformed }
        return r["result"]
      }
    }
  }
  private func codex(_ install: Installation) async -> QueryResult {
    var process: Subprocess?
    do {
      let p = try Subprocess(
        executable: install.executable,
        arguments: ["app-server", "--stdio", "-c", "analytics.enabled=false"],
        environment: discovery.childEnvironment, directory: discovery.directory)
      process = p
      _ = try await rpc(
        p, id: 1, method: "initialize",
        params: .object([
          "clientInfo": .object(["name": .string("agentmeter"), "version": .string("1.0.0")])
        ]))
      try await p.send(.object(["method": .string("initialized")]))
      let before = try Parsers.codexAccount(
        await rpc(p, id: 2, method: "account/read", params: .object(["refreshToken": .bool(false)]))
      )
      var windows: [UsageWindow] = []
      var failure: Failure?
      do {
        windows = try Parsers.codex(
          await rpc(
            p, id: 3, method: "account/rateLimits/read",
            params: .object(["excludeResetCreditDetails": .bool(true)])))
      } catch { failure = Self.failure(error) }
      let after = try Parsers.codexAccount(
        await rpc(p, id: 4, method: "account/read", params: .object(["refreshToken": .bool(false)]))
      )
      guard before.binding == after.binding else { throw Failure.accountChanged }
      let peak = await p.peakRSS
      let group = await p.peakGroupRSS
      await p.close()
      Diagnostics.shared.record("query-group-memory", provider: .codex, value: Int(group))
      Diagnostics.shared.record("query-memory", provider: .codex, value: Int(peak))
      if let failure { return .fail(failure, binding: after.binding) }
      return .success(.init(binding: after.binding, windows: windows, date: Date()))
    } catch {
      if let process { await process.close() }
      return .fail(Self.failure(error))
    }
  }
  private func auth(_ install: Installation) async throws -> AuthIdentity {
    var limits = ProcessLimits()
    limits.seconds = 6
    limits.stdout = 131072
    limits.line = 131072
    let (data, status) = try await Subprocess.run(
      executable: install.executable, arguments: ["auth", "status"],
      environment: claudeEnvironment, directory: discovery.directory, limits: limits)
    return try Parsers.claudeAccount(J.parse(data), status: status)
  }
  private func control(_ p: Subprocess, id: String, request: [String: J]) async throws -> J {
    try await p.send(
      .object([
        "type": .string("control_request"), "request_id": .string(id), "request": .object(request),
      ]))
    while true {
      let r = try await p.next()
      if r["type"].string == "control_response" && r["response"]["request_id"].string == id {
        let response = r["response"]
        guard response["subtype"].string == "success", response["response"].object != nil else {
          throw Failure.incompatible
        }
        return response["response"]
      }
    }
  }
  private func claude(_ install: Installation) async -> QueryResult {
    var process: Subprocess?
    do {
      let before = try await auth(install)
      let p = try Subprocess(
        executable: install.executable,
        arguments: [
          "--print", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
          "--no-session-persistence", "--safe-mode", "--setting-sources=", "--strict-mcp-config",
          "--mcp-config", "{\"mcpServers\":{}}",
        ], environment: claudeEnvironment, directory: discovery.directory)
      process = p
      let initialized = try await control(
        p, id: "init", request: ["subtype": .string("initialize"), "hooks": .object([:])])
      try Parsers.verifyClaudeSession(initialized["account"], account: before)
      var windows: [UsageWindow] = []
      var failure: Failure?
      do {
        windows = try Parsers.claude(
          await control(
            p, id: "usage",
            request: ["subtype": .string("get_usage"), "skip_behaviors": .bool(true)]),
          plan: before.plan)
      } catch { failure = Self.failure(error) }
      let peak = await p.peakRSS
      let group = await p.peakGroupRSS
      await p.close()
      Diagnostics.shared.record("query-group-memory", provider: .claude, value: Int(group))
      process = nil
      let after = try await auth(install)
      guard before.binding == after.binding else { throw Failure.accountChanged }
      Diagnostics.shared.record("query-memory", provider: .claude, value: Int(peak))
      if let failure { return .fail(failure, binding: after.binding) }
      return .success(.init(binding: after.binding, windows: windows, date: Date()))
    } catch {
      if let process { await process.close() }
      return .fail(Self.failure(error))
    }
  }
}
