import CryptoKit
import Foundation

struct AuthIdentity: Sendable, Equatable {
  let email: String
  let organization: String
  let organizationName: String
  let plan: String
  let kind: String
  var binding: String {
    SHA256.hash(data: Data([kind, email, organization, plan].joined(separator: "\n").utf8)).map {
      String(format: "%02x", $0)
    }.joined()
  }
}
enum Parsers {
  static func text(_ j: J, max: Int = 320) throws -> String {
    guard let s = j.string, !s.isEmpty, s.count <= max,
      s == s.trimmingCharacters(in: .whitespacesAndNewlines),
      !s.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) })
    else { throw Failure.malformed }
    return s
  }
  static func email(_ j: J) throws -> String {
    let s = try text(j)
    guard s.contains("@"), !s.contains(where: \.isWhitespace) else { throw Failure.incompatible }
    return s.lowercased()
  }
  static func codexAccount(_ j: J) throws -> AuthIdentity {
    guard let object = j.object, object.keys.contains("account") else { throw Failure.malformed }
    let a = j["account"]
    if a == .null { throw Failure.signedOut }
    let type = try text(a["type"])
    guard ["chatgpt", "chatgptAuthTokens"].contains(type) else { throw Failure.incompatible }
    return try .init(
      email: email(a["email"]), organization: "", organizationName: "",
      plan: text(a["planType"], max: 80), kind: type)
  }
  static func claudeAccount(_ j: J, status: Int32) throws -> AuthIdentity {
    guard let logged = j["loggedIn"].bool else { throw Failure.malformed }
    if !logged {
      guard [0, 1].contains(status) else { throw Failure.processExited }
      throw Failure.signedOut
    }
    guard status == 0 else { throw Failure.malformed }
    guard j["authMethod"].string == "claude.ai", j["apiProvider"].string == "firstParty",
      let plan = j["subscriptionType"].string, ["pro", "max", "team", "enterprise"].contains(plan)
    else { throw Failure.incompatible }
    guard j["analyticsDisabled"].bool == false else { throw Failure.incompatible }
    let org = try text(j["orgId"], max: 160)
    guard UUID(uuidString: org) != nil else { throw Failure.incompatible }
    return try .init(
      email: email(j["email"]), organization: org, organizationName: text(j["orgName"]), plan: plan,
      kind: "claude.ai")
  }
  static func verifyClaudeSession(_ j: J, account: AuthIdentity) throws {
    guard try email(j["email"]) == account.email, j["apiProvider"].string == "firstParty",
      [account.organization, account.organizationName].contains(j["organization"].string ?? ""),
      j["apiKeySource"] == .null || j["apiKeySource"].string == "none"
    else { throw Failure.accountChanged }
    if let source = j["tokenSource"].string,
      !["claude.ai", "oauth", "claudeAiOauth", "CLAUDE_CODE_OAUTH_TOKEN"].contains(source)
    {
      throw Failure.incompatible
    }
  }
  static func percent(_ j: J) throws -> Double? {
    if j == .null { return nil }
    guard let n = j.number, n.isFinite, (0...100).contains(n) else { throw Failure.malformed }
    return n
  }
  static func unix(_ j: J) throws -> Date? {
    if j == .null { return nil }
    guard let n = j.number, n.rounded() == n, n > 0, n < 253_402_300_800 else {
      throw Failure.malformed
    }
    return Date(timeIntervalSince1970: n)
  }
  static func iso(_ j: J) throws -> Date? {
    if j == .null { return nil }
    let s = try text(j, max: 64)
    guard
      s.range(
        of: #"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$"#,
        options: .regularExpression) != nil
    else { throw Failure.malformed }
    let formatter = ISO8601DateFormatter()
    formatter.formatOptions =
      s.contains(".") ? [.withInternetDateTime, .withFractionalSeconds] : [.withInternetDateTime]
    guard let date = formatter.date(from: s) else { throw Failure.malformed }
    // Validate calendar components explicitly; parsers must not normalize February 30.
    let parts = Array(s.prefix(19).utf8)
    func n(_ a: Int, _ b: Int) -> Int { Int(String(decoding: parts[a..<b], as: UTF8.self)) ?? -1 }
    let y = n(0, 4)
    let m = n(5, 7)
    let d = n(8, 10)
    let h = n(11, 13)
    let mi = n(14, 16)
    let sec = n(17, 19)
    let leap = y % 4 == 0 && (y % 100 != 0 || y % 400 == 0)
    let days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
    guard y > 0, (1...12).contains(m), (1...days[m - 1]).contains(d), (0...23).contains(h),
      (0...59).contains(mi), (0...59).contains(sec)
    else { throw Failure.malformed }
    if !s.hasSuffix("Z") {
      let zone = String(s.suffix(6))
      let hh = Int(zone.dropFirst().prefix(2)) ?? 99
      let mm = Int(zone.suffix(2)) ?? 99
      guard hh <= 14, mm <= 59, hh < 14 || mm == 0 else { throw Failure.malformed }
    }
    return date
  }
  static func codex(_ j: J) throws -> [UsageWindow] {
    guard let root = j.object else { throw Failure.malformed }
    let buckets: [String: J]
    if root.keys.contains("rateLimitsByLimitId"), j["rateLimitsByLimitId"] != .null {
      guard let map = j["rateLimitsByLimitId"].object else { throw Failure.malformed }
      buckets = map
    } else {
      guard j["rateLimits"].object != nil else { throw Failure.incompatible }
      buckets = [j["rateLimits"]["limitId"].string ?? "codex": j["rateLimits"]]
    }
    guard buckets.count <= 32 else { throw Failure.outputLimit }
    var result: [UsageWindow] = []
    for key in buckets.keys.sorted() {
      _ = try text(.string(key), max: 120)
      let bucket = buckets[key]!
      guard bucket.object != nil else { throw Failure.malformed }
      if bucket["limitId"] != .null && bucket["limitId"].string != key { throw Failure.malformed }
      let label = key == "codex" ? "Main" : (try? text(bucket["limitName"], max: 100)) ?? key
      for slot in ["primary", "secondary"] {
        let w = bucket[slot]
        if w == .null { continue }
        guard w.object != nil else { throw Failure.malformed }
        let minutes: Int?
        if w["windowDurationMins"] == .null {
          minutes = nil
        } else {
          guard let n = w["windowDurationMins"].number, n.rounded() == n, n > 0, n < 5_256_000
          else { throw Failure.malformed }
          minutes = Int(n)
        }
        result.append(
          try .init(
            id: key + ":" + slot, bucket: key, label: label, durationMinutes: minutes,
            used: percent(w["usedPercent"]), reset: unix(w["resetsAt"])))
      }
    }
    guard !result.isEmpty else { throw Failure.incompatible }
    return result
  }
  static func claude(_ j: J, plan: String) throws -> [UsageWindow] {
    guard j.object != nil, j["rate_limits_available"].bool == true,
      j.object?.keys.contains("behaviors") == true, j["behaviors"] == .null,
      let limits = j["rate_limits"].object
    else { throw Failure.incompatible }
    guard j["subscription_type"].string == plan else { throw Failure.accountChanged }
    guard j["session"]["total_cost_usd"].number == 0,
      j["session"]["total_api_duration_ms"].number == 0,
      j["session"]["model_usage"].object?.isEmpty == true
    else { throw Failure.incompatible }
    guard limits.count <= 64 else { throw Failure.outputLimit }
    var result: [UsageWindow] = []
    for key in limits.keys.sorted() where key != "model_scoped" && key != "extra_usage" {
      let w = limits[key]!
      if w == .null { continue }
      guard let object = w.object,
        object.keys.contains("utilization") || object.keys.contains("resets_at")
      else { continue }
      let minutes = key == "five_hour" ? 300 : (key.hasPrefix("seven_day") ? 10080 : nil)
      result.append(
        try .init(
          id: key, bucket: "claude",
          label: try text(.string(key), max: 100).replacingOccurrences(of: "_", with: " "),
          durationMinutes: minutes, used: percent(w["utilization"]), reset: iso(w["resets_at"])))
    }
    if let scoped = limits["model_scoped"], scoped != .null {
      guard let a = scoped.array, a.count <= 32 else { throw Failure.malformed }
      var seen = Set<String>()
      for w in a {
        let name = try text(w["display_name"], max: 100)
        guard seen.insert(name).inserted else { throw Failure.malformed }
        result.append(
          try .init(
            id: "model:" + name, bucket: "claude", label: name, durationMinutes: nil,
            used: percent(w["utilization"]), reset: iso(w["resets_at"])))
      }
    }
    guard result.contains(where: { $0.used != nil || $0.reset != nil }) else {
      throw Failure.incompatible
    }
    return result
  }
}
