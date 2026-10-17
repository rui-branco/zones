import Foundation
import XCTest
import ZonesCore

final class DisplayLayoutsTests: XCTestCase {
    /// Focus with the middle zone at the given share.
    func focus(_ middle: Double) -> ZoneLayout {
        let side = (1 - middle) / 2
        return ZoneLayout(root: .run(1, columns: true, [
            ZoneNode(size: side), ZoneNode(size: middle), ZoneNode(size: side),
        ]))
    }

    func roundTrip(_ json: Any) throws -> Any {
        let data = try JSONSerialization.data(withJSONObject: json)
        return try JSONSerialization.jsonObject(with: data)
    }

    func testJSONRoundTrip() throws {
        let display = DisplayLayouts(sizes: [focus(0.5), focus(0.6), focus(0.7)], active: 2)
        let back = try XCTUnwrap(DisplayLayouts.fromJSON(try roundTrip(display.toJSON())))
        XCTAssertEqual(back.sizes.count, 3)
        XCTAssertEqual(back.active, 2)
        for i in 0..<3 {
            XCTAssertTrue(back.sizes[i].sameAs(display.sizes[i]))
        }
        XCTAssertTrue(back.current.sameAs(focus(0.7)))
        XCTAssertEqual(back.sizes[0].presetIndex(), 2)
    }

    func testBareLayoutIsOneSize() throws {
        let back = try XCTUnwrap(DisplayLayouts.fromJSON(try roundTrip(ZoneLayout.preset(1).toJSON())))
        XCTAssertEqual(back.sizes.count, 1)
        XCTAssertEqual(back.active, 0)
        XCTAssertEqual(back.current.presetIndex(), 1)
    }

    func testActiveClamps() {
        let a = ZoneLayout.preset(0), b = ZoneLayout.preset(1)
        var display = DisplayLayouts(sizes: [a, b], active: 5)
        XCTAssertTrue(display.current === b)
        XCTAssertEqual(display.active, 1)
        display.active = -3
        XCTAssertTrue(display.current === a)
        XCTAssertEqual(display.active, 0)

        display.active = 1
        display.sizes.removeLast()
        XCTAssertTrue(display.current === a)
        XCTAssertEqual(DisplayLayouts.fromJSON(["active": 9, "sizes": [a.toJSON()]])?.active, 0)
    }

    func testNeverEmpty() {
        var display = DisplayLayouts(sizes: [], active: 0)
        XCTAssertEqual(display.sizes.count, 1)
        display.sizes = []
        XCTAssertEqual(display.sizes.count, 1)
        XCTAssertEqual(display.current.presetIndex(), 0)
    }

    func testRejectsMalformed() throws {
        XCTAssertNil(DisplayLayouts.fromJSON(try roundTrip(["active": 0, "sizes": [] as [Any]])))
        XCTAssertNil(DisplayLayouts.fromJSON(["active": 0]))
        XCTAssertNil(DisplayLayouts.fromJSON([String: Any]()))
        XCTAssertNil(DisplayLayouts.fromJSON(["sizes": "Halves"]))
        XCTAssertNil(DisplayLayouts.fromJSON(["sizes": NSNull()]))
        XCTAssertNil(DisplayLayouts.fromJSON(["sizes": [ZoneLayout.preset(0).toJSON(), ["size": -1]]]))
        XCTAssertNil(DisplayLayouts.fromJSON("Halves"))
        XCTAssertNil(DisplayLayouts.fromJSON(nil))
        // A bad active is just the first size.
        XCTAssertEqual(DisplayLayouts.fromJSON(["active": "two", "sizes": [ZoneLayout.preset(0).toJSON()]])?.active, 0)
    }
}
