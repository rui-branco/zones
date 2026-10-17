import CoreGraphics
import Foundation
import XCTest
import ZonesCore

final class ZoneLayoutTests: XCTestCase {
    let area = CGRect(x: 0, y: 0, width: 1200, height: 600)

    /// Through real JSON bytes, so the NSNumbers JSONSerialization hands back
    /// get read, not just the Swift values toJSON() made.
    func roundTrip(_ json: Any) throws -> Any {
        let data = try JSONSerialization.data(withJSONObject: json)
        return try JSONSerialization.jsonObject(with: data)
    }

    // MARK: - Editing

    func testSplitAndRemove() {
        let layout = ZoneLayout.preset(0)
        XCTAssertEqual(layout.zoneCount, 2)
        XCTAssertEqual(layout.presetIndex(), 0)

        // Same direction as the run: one more sibling, not a nested run.
        XCTAssertTrue(layout.split(zone: 1, columns: true, at: 0.5))
        XCTAssertEqual(layout.zoneCount, 3)
        XCTAssertEqual(layout.root.children?.count, 3)
        XCTAssertTrue(layout.root.children!.allSatisfy { $0.isZone })
        XCTAssertEqual(layout.zonesIn(area, spacing: 0).map { $0.width }, [600, 300, 300])

        // Across the run: the zone becomes a stack.
        XCTAssertTrue(layout.split(zone: 0, columns: false, at: 0.5))
        XCTAssertEqual(layout.zoneCount, 4)
        let stack = layout.root.children![0]
        XCTAssertFalse(stack.isZone)
        XCTAssertFalse(stack.columns)
        XCTAssertEqual(stack.children?.count, 2)
        let zones = layout.zonesIn(area, spacing: 0)
        XCTAssertEqual(zones[0], CGRect(x: 0, y: 0, width: 600, height: 300))
        XCTAssertEqual(zones[1], CGRect(x: 0, y: 300, width: 600, height: 300))

        // Taking one out of the stack leaves a run of one, which folds back to a zone.
        XCTAssertTrue(layout.remove(zone: 1))
        XCTAssertEqual(layout.zoneCount, 3)
        XCTAssertTrue(layout.root.children![0].isZone)
        XCTAssertEqual(layout.zonesIn(area, spacing: 0).map { $0.width }, [600, 300, 300])

        XCTAssertTrue(layout.remove(zone: 0))
        XCTAssertEqual(layout.zonesIn(area, spacing: 0).map { $0.width }, [900, 300])
        XCTAssertTrue(layout.remove(zone: 0))
        XCTAssertEqual(layout.zoneCount, 1)
        XCTAssertTrue(layout.root.isZone)
        XCTAssertEqual(layout.zonesIn(area, spacing: 0), [area])

        // The last zone stays.
        XCTAssertFalse(layout.remove(zone: 0))
        XCTAssertEqual(layout.zoneCount, 1)
    }

    func testSplitRejectsBadArguments() {
        let layout = ZoneLayout.preset(0)
        XCTAssertFalse(layout.split(zone: 0, columns: true, at: 0))
        XCTAssertFalse(layout.split(zone: 0, columns: true, at: 1))
        XCTAssertFalse(layout.split(zone: 0, columns: true, at: .nan))
        XCTAssertFalse(layout.split(zone: 5, columns: true, at: 0.5))
        XCTAssertFalse(layout.remove(zone: -1))
        XCTAssertEqual(layout.presetIndex(), 0)
    }

    // MARK: - Geometry

    func testSpacingEvenGap() {
        let zones = ZoneLayout.preset(1).zonesIn(area, spacing: 8)
        XCTAssertEqual(zones.count, 3)
        XCTAssertEqual(zones[0].minX, 8)
        XCTAssertEqual(zones[2].maxX, 1192)
        XCTAssertEqual(zones[1].minX - zones[0].maxX, 8)
        XCTAssertEqual(zones[2].minX - zones[1].maxX, 8)
        for z in zones {
            XCTAssertEqual(z.minY, 8)
            XCTAssertEqual(z.maxY, 592)
        }
    }

    func testSpacingOddGap() {
        let zones = ZoneLayout.preset(1).zonesIn(area, spacing: 7)
        XCTAssertEqual(zones[0].minX, 7)
        XCTAssertEqual(zones[2].maxX, 1193)
        XCTAssertEqual(zones[1].minX - zones[0].maxX, 7)
        XCTAssertEqual(zones[2].minX - zones[1].maxX, 7)
        // The odd point goes to the left zone's side of the gap.
        XCTAssertEqual(zones[0].maxX, 396)
        XCTAssertEqual(zones[1].minX, 403)
    }

    func testOffsetAndFractionalArea() {
        // A second display up and to the left, its visible frame on half points.
        let zones = ZoneLayout.preset(0).zonesIn(CGRect(x: -1200, y: 24.5, width: 1200, height: 575.5), spacing: 0)
        XCTAssertEqual(zones, [
            CGRect(x: -1200, y: 25, width: 600, height: 575),
            CGRect(x: -600, y: 25, width: 600, height: 575),
        ])
        for z in zones {
            XCTAssertEqual(z.minX, z.minX.rounded())
            XCTAssertEqual(z.minY, z.minY.rounded())
        }
    }

    func testDividers() {
        let layout = ZoneLayout.preset(1)
        var dividers = layout.dividers(in: area)
        XCTAssertEqual(dividers.map { $0.position }, [400, 800])
        XCTAssertTrue(dividers.allSatisfy { $0.vertical })
        XCTAssertEqual(dividers[0].region, area)
        XCTAssertEqual(dividers[0].line, CGRect(x: 400, y: 0, width: 0, height: 600))

        var first = dividers[0]
        layout.moveDivider(&first, to: 300, minLength: 50)
        XCTAssertEqual(first.position, 300)
        XCTAssertEqual(layout.zonesIn(area, spacing: 0).map { $0.width }, [300, 500, 400])
        dividers = layout.dividers(in: area)
        XCTAssertEqual(dividers.map { $0.position }, [300, 800])

        // Past the next line: the middle stops at the minimum.
        first = dividers[0]
        layout.moveDivider(&first, to: 790, minLength: 50)
        XCTAssertEqual(first.position, 750)
        XCTAssertEqual(layout.zonesIn(area, spacing: 0).map { $0.width }, [750, 50, 400])
        XCTAssertEqual(layout.dividers(in: area).map { $0.position }, [750, 800])

        // Nonsense leaves it alone.
        layout.moveDivider(&first, to: .nan, minLength: 50)
        XCTAssertEqual(layout.zonesIn(area, spacing: 0).map { $0.width }, [750, 50, 400])
    }

    func testGridDividers() {
        let dividers = ZoneLayout.preset(5).dividers(in: area)
        XCTAssertEqual(dividers.filter { $0.vertical }.count, 1)
        XCTAssertEqual(dividers.filter { !$0.vertical }.count, 2)
        let vertical = dividers.first { $0.vertical }!
        XCTAssertEqual(vertical.position, 600)
        XCTAssertEqual(vertical.line, CGRect(x: 600, y: 0, width: 0, height: 600))
        let horizontal = dividers.filter { !$0.vertical }
        XCTAssertEqual(horizontal.map { $0.position }, [300, 300])
        XCTAssertEqual(horizontal.map { $0.region.minX }, [0, 600])
        XCTAssertEqual(horizontal[1].line, CGRect(x: 600, y: 300, width: 600, height: 0))
    }

    // MARK: - Presets

    func testEveryPresetRecognizesItself() {
        XCTAssertEqual(ZoneLayout.presetNames.count, 8)
        for i in 0..<ZoneLayout.presetNames.count {
            XCTAssertEqual(ZoneLayout.preset(i).presetIndex(), i, ZoneLayout.presetNames[i])
            XCTAssertEqual(ZoneLayout.preset(i).clone().presetIndex(), i, ZoneLayout.presetNames[i])
        }
    }

    func testDefaults() {
        XCTAssertEqual(ZoneLayout.defaultLayout(for: CGSize(width: 3440, height: 1400)).presetIndex(), 2)
        XCTAssertEqual(ZoneLayout.defaultLayout(for: CGSize(width: 1080, height: 1880)).presetIndex(), 7)
        XCTAssertEqual(ZoneLayout.defaultLayout(for: CGSize(width: 1512, height: 944)).presetIndex(), 0)
        XCTAssertEqual(ZoneLayout.defaultLayout(for: .zero).presetIndex(), 0)
    }

    // MARK: - JSON

    func testJSONRoundTrip() throws {
        let layout = ZoneLayout.preset(4)
        XCTAssertTrue(layout.split(zone: 1, columns: true, at: 0.3))
        var main = layout.dividers(in: area).first { $0.run === layout.root }!
        layout.moveDivider(&main, to: 800, minLength: 50)
        XCTAssertEqual(layout.presetIndex(), -1)

        let back = try XCTUnwrap(ZoneLayout.fromJSON(try roundTrip(layout.toJSON())))
        XCTAssertTrue(back.sameAs(layout))
        XCTAssertEqual(back.zoneCount, 4)
        XCTAssertEqual(back.presetIndex(), -1)
        XCTAssertEqual(back.zonesIn(area, spacing: 8), layout.zonesIn(area, spacing: 8))

        // Straight from the Swift values too, without the bytes in between.
        XCTAssertTrue(try XCTUnwrap(ZoneLayout.fromJSON(layout.toJSON())).sameAs(layout))
    }

    func testJSONSchema() {
        let json = ZoneLayout.preset(0).toJSON()
        XCTAssertEqual(json["size"] as? Double, 1)
        XCTAssertEqual(json["columns"] as? Bool, true)
        let kids = json["children"] as? [[String: Any]]
        XCTAssertEqual(kids?.count, 2)
        XCTAssertEqual(kids?[0]["size"] as? Double, 0.5)
        XCTAssertNil(kids?[0]["columns"])
        XCTAssertNil(kids?[0]["children"])
    }

    func testJSONRejectsMalformed() throws {
        XCTAssertNil(ZoneLayout.fromJSON(["size": -1]))
        XCTAssertNil(ZoneLayout.fromJSON(try roundTrip([
            "size": 1, "columns": true, "children": [["size": -0.5], ["size": 0.5]],
        ])))
        XCTAssertNil(ZoneLayout.fromJSON(["size": 0]))
        XCTAssertNil(ZoneLayout.fromJSON(["size": Double.nan]))
        XCTAssertNil(ZoneLayout.fromJSON(["size": Double.infinity]))
        XCTAssertNil(ZoneLayout.fromJSON(nil))
        XCTAssertNil(ZoneLayout.fromJSON("Halves"))
        XCTAssertNil(ZoneLayout.fromJSON(42))
        XCTAssertNil(ZoneLayout.fromJSON([1, 2]))
        XCTAssertNil(ZoneLayout.fromJSON(NSNull()))
        XCTAssertNil(ZoneLayout.fromJSON(["size": 1, "children": [["size": 1], "zone"]]))
    }

    func testJSONToleratesOddValues() throws {
        // A share that is not a number reads as a whole one, like on Windows.
        let layout = try XCTUnwrap(ZoneLayout.fromJSON(try roundTrip([
            "size": 1, "columns": true, "children": [["size": true], ["size": "big"], ["size": NSNull()]],
        ])))
        XCTAssertEqual(layout.presetIndex(), 1)
        // A bad "columns" falls back to side by side; a bad "children" means a zone.
        XCTAssertEqual(ZoneLayout.fromJSON(["size": 1, "columns": 1, "children": [["size": 1], ["size": 1]]])?.presetIndex(), 0)
        XCTAssertEqual(ZoneLayout.fromJSON(["size": 1, "children": "lots"])?.zoneCount, 1)
    }

    func testNestedSameAxisRunNormalizesToHalves() throws {
        let json: [String: Any] = [
            "size": 1, "columns": true, "children": [
                ["size": 1, "columns": true, "children": [["size": 1], ["size": 1]]],
            ],
        ]
        let layout = try XCTUnwrap(ZoneLayout.fromJSON(try roundTrip(json)))
        XCTAssertEqual(layout.presetIndex(), 0)
        XCTAssertEqual(layout.root.children?.count, 2)
    }

    func testJSONDepthLimit() throws {
        func nest(_ levels: Int) -> [String: Any] {
            if levels == 0 { return ["size": 1] }
            return ["size": 1, "columns": levels % 2 == 0, "children": [nest(levels - 1), ["size": 1]]]
        }
        XCTAssertEqual(ZoneLayout.fromJSON(try roundTrip(nest(12)))?.zoneCount, 13)
        XCTAssertNil(ZoneLayout.fromJSON(try roundTrip(nest(13))))
    }

    func testNaNIsNeverWritten() throws {
        let layout = ZoneLayout.preset(0)
        layout.root.children![0].size = .nan
        let json = layout.toJSON()
        XCTAssertTrue(JSONSerialization.isValidJSONObject(json))
        XCTAssertTrue((json["children"] as? [[String: Any]])?[0]["size"] is NSNull)
        // The lost share reads back as a whole one beside the other's half.
        XCTAssertEqual(ZoneLayout.fromJSON(try roundTrip(json))?.presetIndex(), 3)
    }
}
