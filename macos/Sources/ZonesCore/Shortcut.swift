import Foundation

/// A global keyboard shortcut in Carbon terms (virtual key code + Carbon
/// modifier mask: cmdKey 0x100, shiftKey 0x200, optionKey 0x800, controlKey 0x1000).
///
/// Carbon terms because RegisterEventHotKey is what takes a global shortcut
/// without needing Accessibility or Input Monitoring for it, and it wants
/// exactly these.
public struct Shortcut: Equatable {
    public var keyCode: UInt32
    public var modifiers: UInt32

    public init(keyCode: UInt32, modifiers: UInt32) {
        self.keyCode = keyCode
        self.modifiers = modifiers
    }

    static let command: UInt32 = 0x100
    static let shift: UInt32 = 0x200
    static let option: UInt32 = 0x800
    static let control: UInt32 = 0x1000

    /// Every modifier a shortcut can carry; anything else in a stored mask is dropped.
    static let allModifiers: UInt32 = command | shift | option | control

    /// The shortcut as the symbols and key name to draw, in the order macOS
    /// menus show them: ⌃⌥⇧⌘, then the key.
    public var parts: [String] {
        var parts: [String] = []
        if modifiers & Shortcut.control != 0 { parts.append("⌃") }
        if modifiers & Shortcut.option != 0 { parts.append("⌥") }
        if modifiers & Shortcut.shift != 0 { parts.append("⇧") }
        if modifiers & Shortcut.command != 0 { parts.append("⌘") }
        parts.append(Shortcut.keyNames[keyCode] ?? String(format: "Key 0x%02X", keyCode))
        return parts
    }

    /// Names by virtual key code (kVK_*). Letters and digits are where they
    /// sit on an ANSI (US) keyboard, which is what the codes are named after.
    private static let keyNames: [UInt32: String] = [
        0x00: "A", 0x0B: "B", 0x08: "C", 0x02: "D", 0x0E: "E", 0x03: "F", 0x05: "G",
        0x04: "H", 0x22: "I", 0x26: "J", 0x28: "K", 0x25: "L", 0x2E: "M", 0x2D: "N",
        0x1F: "O", 0x23: "P", 0x0C: "Q", 0x0F: "R", 0x01: "S", 0x11: "T", 0x20: "U",
        0x09: "V", 0x0D: "W", 0x07: "X", 0x10: "Y", 0x06: "Z",
        0x1D: "0", 0x12: "1", 0x13: "2", 0x14: "3", 0x15: "4", 0x17: "5", 0x16: "6",
        0x1A: "7", 0x1C: "8", 0x19: "9",
        0x7A: "F1", 0x78: "F2", 0x63: "F3", 0x76: "F4", 0x60: "F5", 0x61: "F6",
        0x62: "F7", 0x64: "F8", 0x65: "F9", 0x6D: "F10", 0x67: "F11", 0x6F: "F12",
        0x69: "F13", 0x6B: "F14", 0x71: "F15", 0x6A: "F16", 0x40: "F17", 0x4F: "F18",
        0x50: "F19", 0x5A: "F20",
        0x7B: "←", 0x7E: "↑", 0x7C: "→", 0x7D: "↓",
        0x31: "Space", 0x24: "Return", 0x30: "Tab", 0x33: "Delete", 0x35: "Esc",
        0x73: "Home", 0x77: "End", 0x74: "Page Up", 0x79: "Page Down",
    ]
}
