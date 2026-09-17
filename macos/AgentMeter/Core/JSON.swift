import CoreFoundation
import Foundation

indirect enum J: Sendable, Equatable {
  case object([String: J])
  case array([J])
  case string(String)
  case number(Double)
  case bool(Bool)
  case null
  subscript(_ key: String) -> J {
    if case .object(let o) = self { return o[key] ?? .null }
    return .null
  }
  var object: [String: J]? {
    if case .object(let x) = self { return x }
    return nil
  }
  var array: [J]? {
    if case .array(let x) = self { return x }
    return nil
  }
  var string: String? {
    if case .string(let x) = self { return x }
    return nil
  }
  var number: Double? {
    if case .number(let x) = self { return x }
    return nil
  }
  var bool: Bool? {
    if case .bool(let x) = self { return x }
    return nil
  }
  func encoded() throws -> Data {
    try JSONSerialization.data(
      withJSONObject: foundation, options: [.fragmentsAllowed, .sortedKeys])
  }
  private var foundation: Any {
    switch self {
    case .object(let x): x.mapValues(\.foundation)
    case .array(let x): x.map(\.foundation)
    case .string(let x): x
    case .number(let x): x
    case .bool(let x): x
    case .null: NSNull()
    }
  }
  static func parse(_ data: Data) throws -> J {
    guard data.count <= 262144 else { throw Failure.outputLimit }
    var p = Parser(bytes: Array(data))
    let value = try p.value(0)
    p.space()
    guard p.i == p.bytes.count else { throw Failure.malformed }
    return value
  }
  private struct Parser {
    let bytes: [UInt8]
    var i = 0
    var nodes = 0
    mutating func space() {
      while i < bytes.count && [9, 10, 13, 32].contains(bytes[i]) { i += 1 }
    }
    mutating func take(_ c: UInt8) -> Bool {
      space()
      if i < bytes.count && bytes[i] == c {
        i += 1
        return true
      }
      return false
    }
    mutating func text() throws -> String {
      space()
      let start = i
      guard i < bytes.count && bytes[i] == 34 else { throw Failure.malformed }
      i += 1
      while i < bytes.count {
        if bytes[i] == 92 {
          i += 2
          continue
        }
        if bytes[i] == 34 {
          i += 1
          guard
            let s = try JSONSerialization.jsonObject(
              with: Data(bytes[start..<i]), options: .fragmentsAllowed) as? String
          else { throw Failure.malformed }
          return s
        }
        if bytes[i] < 32 { throw Failure.malformed }
        i += 1
      }
      throw Failure.malformed
    }
    mutating func value(_ depth: Int) throws -> J {
      nodes += 1
      guard depth <= 32 && nodes <= 10000 else { throw Failure.outputLimit }
      space()
      guard i < bytes.count else { throw Failure.malformed }
      if take(123) {
        var o: [String: J] = [:]
        if take(125) { return .object(o) }
        repeat {
          let k = try text()
          guard o[k] == nil && take(58) else { throw Failure.malformed }
          o[k] = try value(depth + 1)
          if take(125) { return .object(o) }
        } while take(44)
        throw Failure.malformed
      }
      if take(91) {
        var a: [J] = []
        if take(93) { return .array(a) }
        repeat {
          a.append(try value(depth + 1))
          if take(93) { return .array(a) }
        } while take(44)
        throw Failure.malformed
      }
      if bytes[i] == 34 { return .string(try text()) }
      for (literal, v) in [("true", J.bool(true)), ("false", J.bool(false)), ("null", J.null)] {
        let b = Array(literal.utf8)
        if bytes[i...].starts(with: b) {
          i += b.count
          return v
        }
      }
      let start = i
      while i < bytes.count && ![9, 10, 13, 32, 44, 93, 125].contains(bytes[i]) { i += 1 }
      guard i > start,
        let n = try? JSONSerialization.jsonObject(
          with: Data(bytes[start..<i]), options: .fragmentsAllowed) as? NSNumber,
        CFGetTypeID(n) != CFBooleanGetTypeID(), n.doubleValue.isFinite
      else { throw Failure.malformed }
      return .number(n.doubleValue)
    }
  }
}
