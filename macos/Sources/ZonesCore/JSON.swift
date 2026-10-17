import Foundation

/// Reading values out of what JSONSerialization hands back, without trusting it.
///
/// It returns every number and every true/false alike as NSNumber, and Swift
/// will read a true as 1 and a 1 as true, so a hand-edited file could turn a
/// setting on with a number or give a zone a share of `true`. These tell the
/// two apart, the way the Windows reader does.
enum JSON {
    static func isBool(_ n: NSNumber) -> Bool {
        CFGetTypeID(n) == CFBooleanGetTypeID()
    }

    /// A JSON number, nil for anything else (a true/false included).
    static func number(_ v: Any?) -> Double? {
        guard let n = v as? NSNumber, !isBool(n) else { return nil }
        return n.doubleValue
    }

    /// A JSON true/false, nil for anything else (a 0 or 1 included).
    static func bool(_ v: Any?) -> Bool? {
        guard let n = v as? NSNumber, isBool(n) else { return nil }
        return n.boolValue
    }

    /// A whole number, cut toward zero like the Windows reader's cast. Nil
    /// when it does not fit an Int, where Swift would trap instead.
    static func int(_ v: Any?) -> Int? {
        guard let d = number(v) else { return nil }
        return Int(exactly: d.rounded(.towardZero))
    }
}
