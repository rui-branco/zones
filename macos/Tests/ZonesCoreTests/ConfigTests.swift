import CoreGraphics
import Foundation
import XCTest
import ZonesCore

final class ConfigTests: XCTestCase {
    var scratch: URL!

    /// Scratch files go under the package's own .build folder rather than the
    /// system temp folder, so a test run writes nowhere outside the checkout.
    override func setUp() {
        super.setUp()
        let package = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        scratch = package.appending(component: ".build").appending(component: "test-scratch")
            .appending(component: UUID().uuidString)
    }

    override func tearDown() {
        try? FileManager.default.removeItem(at: scratch)
        super.tearDown()
    }

    func write(_ text: String) throws -> URL {
        let url = scratch.appending(component: "config.json")
        try FileManager.default.createDirectory(at: scratch, withIntermediateDirectories: true)
        try Data(text.utf8).write(to: url)
        return url
    }

    func testDefaults() {
        let cfg = Config()
        XCTAssertTrue(cfg.snapEnabled)
        XCTAssertTrue(cfg.holdShift)
        XCTAssertEqual(cfg.spacing, 8)
        XCTAssertTrue(cfg.layouts.isEmpty)
        XCTAssertEqual(cfg.zoneShortcut, Shortcut(keyCode: 6, modifiers: 0x1000 | 0x800))
        XCTAssertEqual(cfg.sizeShortcut, Shortcut(keyCode: 7, modifiers: 0x1000 | 0x800))
        XCTAssertEqual(cfg.zoneBackShortcut, Shortcut(keyCode: 6, modifiers: 0x1000 | 0x800 | 0x200))
        XCTAssertEqual(cfg.sizeBackShortcut, Shortcut(keyCode: 7, modifiers: 0x1000 | 0x800 | 0x200))
        XCTAssertFalse(cfg.startAtLogin)
        XCTAssertTrue(cfg.autoUpdate)
        XCTAssertTrue(Config.defaultURL.path.hasSuffix("/Library/Application Support/Zones/config.json"))
    }

    func testSaveAndLoad() throws {
        let cfg = Config()
        cfg.snapEnabled = false
        cfg.holdShift = false
        cfg.spacing = 12
        cfg.startAtLogin = true
        cfg.autoUpdate = false
        cfg.zoneShortcut = nil
        cfg.sizeShortcut = Shortcut(keyCode: 0x28, modifiers: 0x100 | 0x200)   // ⌘⇧K
        cfg.layouts["37D8832A-2D66-02CA-B9F7-8F30A301B230"] = DisplayLayouts(
            sizes: [ZoneLayout.preset(2), ZoneLayout.preset(4)], active: 1)
        cfg.layouts["B5A1C2D3-0000-1111-2222-333344445555"] = DisplayLayouts(ZoneLayout.preset(7))

        // A folder that is not there yet: save makes it.
        let url = scratch.appending(component: "nested").appending(component: "config.json")
        XCTAssertTrue(cfg.save(to: url))

        let back = Config.load(from: url)
        XCTAssertFalse(back.snapEnabled)
        XCTAssertFalse(back.holdShift)
        XCTAssertEqual(back.spacing, 12)
        XCTAssertTrue(back.startAtLogin)
        XCTAssertFalse(back.autoUpdate)
        XCTAssertNil(back.zoneShortcut)
        XCTAssertEqual(back.sizeShortcut, Shortcut(keyCode: 0x28, modifiers: 0x100 | 0x200))
        XCTAssertEqual(back.layouts.count, 2)
        let display = try XCTUnwrap(back.layouts["37D8832A-2D66-02CA-B9F7-8F30A301B230"])
        XCTAssertEqual(display.sizes.count, 2)
        XCTAssertEqual(display.active, 1)
        XCTAssertEqual(display.sizes[0].presetIndex(), 2)
        XCTAssertEqual(display.current.presetIndex(), 4)
        XCTAssertEqual(back.layouts["B5A1C2D3-0000-1111-2222-333344445555"]?.current.presetIndex(), 7)

        // The file itself: the agreed keys, sorted and pretty-printed.
        let text = try String(contentsOf: url, encoding: .utf8)
        XCTAssertTrue(text.contains("\n"))
        let root = try XCTUnwrap(try JSONSerialization.jsonObject(with: Data(text.utf8)) as? [String: Any])
        XCTAssertEqual(Set(root.keys), [
            "SnapEnabled", "SnapHoldShift", "SnapSpacing", "SnapLayouts",
            "ZoneShortcut", "SizeShortcut", "ZoneBackShortcut", "SizeBackShortcut", "StartAtLogin", "AutoUpdate",
        ])
        XCTAssertTrue(root["ZoneShortcut"] is NSNull)
        let keys = ["AutoUpdate", "SizeBackShortcut", "SizeShortcut", "SnapEnabled", "SnapHoldShift", "SnapLayouts",
                    "SnapSpacing", "StartAtLogin", "ZoneBackShortcut", "ZoneShortcut"]
        let offsets = keys.map { text.range(of: "\"\($0)\"")!.lowerBound }
        XCTAssertEqual(offsets, offsets.sorted())
    }

    func testMissingAndCorruptGiveDefaults() throws {
        let missing = Config.load(from: scratch.appending(component: "nothing.json"))
        XCTAssertEqual(missing.spacing, 8)
        XCTAssertTrue(missing.snapEnabled)

        for text in ["{not json", "", "[1, 2, 3]", "\"config\"", "null"] {
            let cfg = Config.load(from: try write(text))
            XCTAssertTrue(cfg.snapEnabled, text)
            XCTAssertTrue(cfg.holdShift, text)
            XCTAssertEqual(cfg.spacing, 8, text)
            XCTAssertTrue(cfg.layouts.isEmpty, text)
            XCTAssertEqual(cfg.zoneShortcut, Config().zoneShortcut, text)
            XCTAssertFalse(cfg.startAtLogin, text)
            XCTAssertTrue(cfg.autoUpdate, text)
        }
    }

    func testSpacingClamps() throws {
        XCTAssertEqual(Config.load(from: try write(#"{"SnapSpacing": 99}"#)).spacing, 48)
        XCTAssertEqual(Config.load(from: try write(#"{"SnapSpacing": -4}"#)).spacing, 0)
        XCTAssertEqual(Config.load(from: try write(#"{"SnapSpacing": 1e300}"#)).spacing, 8)
        XCTAssertEqual(Config.load(from: try write(#"{"SnapSpacing": true}"#)).spacing, 8)
        let cfg = Config()
        cfg.spacing = 99
        XCTAssertEqual(cfg.spacing, Config.maxSpacing)
    }

    func testBadPartsCostOnlyThemselves() throws {
        let cfg = Config.load(from: try write("""
        {
            "SnapEnabled": 0,
            "SnapHoldShift": false,
            "SnapSpacing": "wide",
            "AutoUpdate": 0,
            "ZoneShortcut": {"keyCode": "Z"},
            "SizeShortcut": {"keyCode": 3, "modifiers": 4352},
            "SnapLayouts": {
                "GOOD": {"active": 1, "sizes": [{"size": 1}, {"size": 1, "columns": false, "children": [{"size": 1}, {"size": 1}]}]},
                "BARE": {"size": 1, "columns": true, "children": [{"size": 1}, {"size": 1}, {"size": 1}]},
                "BAD": {"size": -1},
                "WORSE": [1, 2]
            }
        }
        """))
        XCTAssertTrue(cfg.snapEnabled)
        XCTAssertFalse(cfg.holdShift)
        XCTAssertEqual(cfg.spacing, 8)
        XCTAssertTrue(cfg.autoUpdate)
        XCTAssertEqual(cfg.zoneShortcut, Config().zoneShortcut)
        XCTAssertEqual(cfg.sizeShortcut, Shortcut(keyCode: 3, modifiers: 0x1000 | 0x100))
        XCTAssertEqual(Set(cfg.layouts.keys), ["GOOD", "BARE"])
        XCTAssertEqual(cfg.layouts["GOOD"]?.current.presetIndex(), 7)
        XCTAssertEqual(cfg.layouts["BARE"]?.current.presetIndex(), 1)
    }

    func testShortcutMasked() throws {
        let cfg = Config.load(from: try write(#"{"ZoneShortcut": {"keyCode": 262, "modifiers": 65535}}"#))
        XCTAssertEqual(cfg.zoneShortcut, Shortcut(keyCode: 6, modifiers: 0x1B00))
    }

    func testDefaultLayoutIsNotStored() {
        let cfg = Config()
        XCTAssertEqual(cfg.layout(for: "WIDE", size: CGSize(width: 3440, height: 1400)).presetIndex(), 2)
        XCTAssertEqual(cfg.layouts(for: "TALL", size: CGSize(width: 1080, height: 1880)).current.presetIndex(), 7)
        XCTAssertTrue(cfg.layouts.isEmpty)

        cfg.layouts["WIDE"] = DisplayLayouts(sizes: [ZoneLayout.preset(1), ZoneLayout.preset(5)], active: 1)
        XCTAssertEqual(cfg.layout(for: "WIDE", size: CGSize(width: 3440, height: 1400)).presetIndex(), 5)
    }

    func testNaNSavesAsValidJSON() throws {
        let cfg = Config()
        let layout = ZoneLayout.preset(0)
        layout.root.children![0].size = .infinity
        cfg.layouts["X"] = DisplayLayouts(layout)
        let url = scratch.appending(component: "config.json")
        XCTAssertTrue(cfg.save(to: url))
        XCTAssertEqual(Config.load(from: url).layouts["X"]?.current.zoneCount, 2)
    }

    func testShortcutParts() {
        XCTAssertEqual(Shortcut(keyCode: 6, modifiers: 0x1000 | 0x800).parts, ["⌃", "⌥", "Z"])
        XCTAssertEqual(Shortcut(keyCode: 0x7B, modifiers: 0x100 | 0x200 | 0x800 | 0x1000).parts, ["⌃", "⌥", "⇧", "⌘", "←"])
        XCTAssertEqual(Shortcut(keyCode: 0x1D, modifiers: 0x100).parts, ["⌘", "0"])
        XCTAssertEqual(Shortcut(keyCode: 0x5A, modifiers: 0).parts, ["F20"])
        XCTAssertEqual(Shortcut(keyCode: 0x79, modifiers: 0x200).parts, ["⇧", "Page Down"])
        XCTAssertEqual(Shortcut(keyCode: 0x18, modifiers: 0x800).parts, ["⌥", "Key 0x18"])
    }
}
