import CoreGraphics
import Foundation

/// The user's settings, kept in config.json. Read and written with
/// JSONSerialization rather than Codable so a malformed part can be skipped
/// on its own: a bad value costs that value, a bad layout that display.
public final class Config {
    /// Whether dragging a window offers snap zones at all.
    public var snapEnabled: Bool = true

    /// Zones show only while Shift is held (true), or on every drag with
    /// Shift holding them back (false).
    public var holdShift: Bool = true

    /// The gap around each zone, in points. Clamped to 0…`maxSpacing` on
    /// every write, so a hand-edited file cannot ask for more.
    public var spacing: Int = 8 {
        didSet { spacing = min(max(spacing, 0), Config.maxSpacing) }
    }

    /// The widest gap the settings window offers, in points.
    public static let maxSpacing = 48

    /// Each display's own layouts, by display key: the display's UUID
    /// string, which stays the same across reconnects and rearranging where
    /// its display ID does not. A display with none here gets the default
    /// for its shape; see `layouts(for:size:)`.
    public var layouts: [String: DisplayLayouts] = [:]

    /// Moves the focused window on to its next zone. Nil for none.
    public var zoneShortcut: Shortcut? = Shortcut(keyCode: 6 /* Z */, modifiers: 0x1000 | 0x800)   // ⌃⌥Z

    /// Flips the display under the focused window to its next size. Nil for none.
    public var sizeShortcut: Shortcut? = Shortcut(keyCode: 7 /* X */, modifiers: 0x1000 | 0x800)   // ⌃⌥X

    /// Moves the focused window back to its previous zone. Its own shortcut,
    /// not the forward one with ⇧, so going back can be put on any key.
    public var zoneBackShortcut: Shortcut? = Shortcut(keyCode: 6, modifiers: 0x1000 | 0x800 | 0x200)   // ⌃⌥⇧Z

    /// Flips the display back to its previous size.
    public var sizeBackShortcut: Shortcut? = Shortcut(keyCode: 7, modifiers: 0x1000 | 0x800 | 0x200)   // ⌃⌥⇧X

    /// Whether to launch at login.
    public var startAtLogin: Bool = false

    public init() {}

    /// ~/Library/Application Support/Zones/config.json
    public static var defaultURL: URL {
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser.appending(path: "Library/Application Support")
        return support.appending(component: "Zones").appending(component: "config.json")
    }

    /// The layouts for a display: the ones picked for it, or the default for
    /// its shape. The default is not stored, so a display keeps following it
    /// until someone actually chooses something.
    public func layouts(for displayKey: String, size: CGSize) -> DisplayLayouts {
        layouts[displayKey] ?? DisplayLayouts(ZoneLayout.defaultLayout(for: size))
    }

    /// The layout a display is using right now.
    public func layout(for displayKey: String, size: CGSize) -> ZoneLayout {
        layouts(for: displayKey, size: size).current
    }

    /// The settings at `url`. A missing or corrupt file must never stop the
    /// app running, so it gives the defaults instead; a value that is there
    /// but unreadable keeps its default.
    public static func load(from url: URL = Config.defaultURL) -> Config {
        let cfg = Config()
        guard let data = try? Data(contentsOf: url),
              let parsed = try? JSONSerialization.jsonObject(with: data),
              let root = parsed as? [String: Any] else { return cfg }

        cfg.snapEnabled = JSON.bool(root["SnapEnabled"]) ?? cfg.snapEnabled
        cfg.holdShift = JSON.bool(root["SnapHoldShift"]) ?? cfg.holdShift
        cfg.spacing = JSON.int(root["SnapSpacing"]) ?? cfg.spacing
        cfg.zoneShortcut = shortcut(root["ZoneShortcut"], or: cfg.zoneShortcut)
        cfg.sizeShortcut = shortcut(root["SizeShortcut"], or: cfg.sizeShortcut)
        cfg.zoneBackShortcut = shortcut(root["ZoneBackShortcut"], or: cfg.zoneBackShortcut)
        cfg.sizeBackShortcut = shortcut(root["SizeBackShortcut"], or: cfg.sizeBackShortcut)
        cfg.startAtLogin = JSON.bool(root["StartAtLogin"]) ?? cfg.startAtLogin

        if let stored = root["SnapLayouts"] as? [String: Any] {
            for (key, value) in stored {
                if let display = DisplayLayouts.fromJSON(value) { cfg.layouts[key] = display }
            }
        }
        return cfg
    }

    /// Writes the settings to `url`, creating its folder. Atomic, so a crash
    /// halfway leaves the old file rather than half a new one. False when it
    /// could not.
    @discardableResult
    public func save(to url: URL = Config.defaultURL) -> Bool {
        var root: [String: Any] = [:]
        root["SnapEnabled"] = snapEnabled
        root["SnapHoldShift"] = holdShift
        root["SnapSpacing"] = spacing
        root["SnapLayouts"] = layouts.mapValues { $0.toJSON() }
        root["ZoneShortcut"] = Config.json(zoneShortcut)
        root["SizeShortcut"] = Config.json(sizeShortcut)
        root["ZoneBackShortcut"] = Config.json(zoneBackShortcut)
        root["SizeBackShortcut"] = Config.json(sizeBackShortcut)
        root["StartAtLogin"] = startAtLogin

        // JSONSerialization raises an Objective-C exception, not a Swift
        // error, on anything it cannot write (a NaN, say): check first.
        guard JSONSerialization.isValidJSONObject(root),
              let data = try? JSONSerialization.data(withJSONObject: root, options: [.prettyPrinted, .sortedKeys])
        else { return false }

        do {
            try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            try data.write(to: url, options: .atomic)
            return true
        } catch {
            return false
        }
    }

    private static func json(_ s: Shortcut?) -> Any {
        guard let s = s else { return NSNull() }
        return ["keyCode": Int(s.keyCode), "modifiers": Int(s.modifiers)]
    }

    /// A stored shortcut: null is none; absent or unreadable keeps `fallback`.
    /// Masked like the Windows reader, so a stray bit cannot reach the hotkey call.
    private static func shortcut(_ v: Any?, or fallback: Shortcut?) -> Shortcut? {
        guard let v = v else { return fallback }
        if v is NSNull { return nil }
        guard let d = v as? [String: Any],
              let key = JSON.int(d["keyCode"]),
              let mods = JSON.int(d["modifiers"]) else { return fallback }
        return Shortcut(keyCode: UInt32(key & 0xFF), modifiers: UInt32(mods & Int(Shortcut.allModifiers)))
    }
}
