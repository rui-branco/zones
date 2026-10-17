import CoreGraphics
import Foundation

/// One piece of a snap layout: either a zone a window can be dropped into
/// (no children), or a run of pieces laid side by side (`columns`) or stacked
/// top to bottom.
///
/// A tree rather than a flat list of rectangles, because that is what makes
/// editing behave: dragging the line between two columns resizes those two
/// and nothing else, and removing a zone hands its space to a neighbour
/// instead of leaving a hole nothing can fill.
public final class ZoneNode {
    /// Share of the parent along the parent's axis. Siblings sum to 1.
    public var size: Double

    /// For a run: children side by side (true) or stacked (false).
    public var columns: Bool = false

    /// Nil, or empty, for a zone.
    public var children: [ZoneNode]?

    public var isZone: Bool {
        children?.isEmpty ?? true
    }

    public init(size: Double) {
        self.size = size
    }

    /// A run of the given pieces.
    public static func run(_ size: Double, columns: Bool, _ children: [ZoneNode]) -> ZoneNode {
        let n = ZoneNode(size: size)
        n.columns = columns
        n.children = children
        return n
    }

    /// A run of `count` equal zones.
    public static func even(_ size: Double, columns: Bool, count: Int) -> ZoneNode {
        // max: a negative count would trap building the range, not just be empty.
        let kids = (0..<max(count, 0)).map { _ in ZoneNode(size: 1.0 / Double(count)) }
        return run(size, columns: columns, kids)
    }

    public func clone() -> ZoneNode {
        let n = ZoneNode(size: size)
        n.columns = columns
        n.children = children?.map { $0.clone() }
        return n
    }
}

/// The line between two neighbouring pieces of a run, in the point space of
/// the rectangle the layout was laid out in. The editor drags these.
public struct ZoneDivider {
    /// The run whose children the line separates.
    public let run: ZoneNode

    /// The line sits between `run.children[index]` and `[index + 1]`.
    public let index: Int

    /// The run's own rectangle. Moving the line never changes it.
    public let region: CGRect

    /// Where the line is: an x for a vertical line, a y for a horizontal one.
    public var position: CGFloat

    /// A vertical line, between two columns.
    public var vertical: Bool {
        run.columns
    }

    /// The line itself, as a zero-width (or zero-height) rectangle.
    public var line: CGRect {
        vertical
            ? CGRect(x: position, y: region.minY, width: 0, height: region.height)
            : CGRect(x: region.minX, y: position, width: region.width, height: 0)
    }
}

/// How one display is carved up into zones. Shares, not points, so the same
/// layout fits whatever resolution and scale the display has, and survives
/// either changing.
///
/// Zones are numbered in reading order of the tree: a run of columns left to
/// right, a stack top to bottom, depth first. Every operation leaves the tree
/// normalised: no run of one, no run inside a run going the same way, and
/// every set of siblings summing to 1.
///
/// Geometry is in the global y-down, top-left-origin space Accessibility and
/// CoreGraphics use, in whole points.
public final class ZoneLayout {
    public var root: ZoneNode

    public init(root: ZoneNode) {
        self.root = root
        normalize()
    }

    public func clone() -> ZoneLayout {
        ZoneLayout(root: root.clone())
    }

    public var zoneCount: Int {
        zones().count
    }

    /// The zone nodes, in zone-number order.
    public func zones() -> [ZoneNode] {
        var list: [ZoneNode] = []
        ZoneLayout.collectZones(root, &list)
        return list
    }

    private static func collectZones(_ n: ZoneNode, _ list: inout [ZoneNode]) {
        guard let kids = n.children, !kids.isEmpty else {
            list.append(n)
            return
        }
        for c in kids { collectZones(c, &list) }
    }

    // MARK: - Geometry

    /// Every zone's rectangle inside `area`, in zone-number order, with
    /// `spacing` points between neighbouring zones and around the outside.
    ///
    /// Laid out in whole points as it goes, so two neighbours share the very
    /// same edge before the spacing is taken off: rounding each zone on its
    /// own left one-point slivers and overlaps between them.
    public func zonesIn(_ area: CGRect, spacing: CGFloat) -> [CGRect] {
        let area = ZoneLayout.whole(area)
        var raw: [CGRect] = []
        var dividers: [ZoneDivider] = []
        ZoneLayout.lay(root, area, &raw, &dividers)

        let spacing = spacing.isFinite ? spacing.rounded() : 0
        if spacing <= 0 { return raw }

        // The outer edge gets the whole gap; an inner edge gets half, so two
        // halves meet as one gap. The odd point goes to the left/top side.
        let inner2 = (spacing / 2).rounded(.down)
        let inner1 = spacing - inner2

        return raw.map { r in
            let l = r.minX + (r.minX <= area.minX ? spacing : inner2)
            let t = r.minY + (r.minY <= area.minY ? spacing : inner2)
            let rr = r.maxX - (r.maxX >= area.maxX ? spacing : inner1)
            let b = r.maxY - (r.maxY >= area.maxY ? spacing : inner1)
            return CGRect(x: l, y: t, width: max(l + 1, rr) - l, height: max(t + 1, b) - t)
        }
    }

    /// Every line between neighbouring pieces, laid out in `area`.
    public func dividers(in area: CGRect) -> [ZoneDivider] {
        var zones: [CGRect] = []
        var list: [ZoneDivider] = []
        ZoneLayout.lay(root, ZoneLayout.whole(area), &zones, &list)
        return list
    }

    /// `area` with each edge on a whole point, so everything laid out in it
    /// is too: a screen's visible frame can sit on a half point.
    private static func whole(_ area: CGRect) -> CGRect {
        let l = area.minX.rounded(), t = area.minY.rounded()
        let r = area.maxX.rounded(), b = area.maxY.rounded()
        return CGRect(x: l, y: t, width: r - l, height: b - t)
    }

    private static func lay(_ n: ZoneNode, _ r: CGRect, _ zones: inout [CGRect], _ dividers: inout [ZoneDivider]) {
        guard let kids = n.children, !kids.isEmpty else {
            zones.append(r)
            return
        }

        let start = n.columns ? r.minX : r.minY
        let extent = n.columns ? r.width : r.height
        var cum = 0.0
        var from = start

        for (i, c) in kids.enumerated() {
            cum += c.size
            // The last child always ends exactly on the run's edge, whatever
            // the sum of the shares has drifted to. To even, as Math.Round does
            // on Windows, so both versions put a line on the same point.
            let to = i == kids.count - 1
                ? start + extent
                : start + (CGFloat(cum) * extent).rounded(.toNearestOrEven)

            let child = n.columns
                ? CGRect(x: from, y: r.minY, width: to - from, height: r.height)
                : CGRect(x: r.minX, y: from, width: r.width, height: to - from)
            lay(c, child, &zones, &dividers)

            if i < kids.count - 1 {
                dividers.append(ZoneDivider(run: n, index: i, region: r, position: to))
            }
            from = to
        }
    }

    // MARK: - Editing

    /// Cuts zone `zone` in two: with a vertical line when `columns`, into a
    /// left and a right part, otherwise into a top and a bottom one. `at` is
    /// where the cut falls inside the zone, 0 to 1. Size limits are the
    /// caller's to check.
    @discardableResult
    public func split(zone: Int, columns: Bool, at: Double) -> Bool {
        guard at > 0 && at < 1, let hit = find(zone) else { return false }
        let node = hit.node

        if let parent = hit.parent, parent.columns == columns,
           let i = parent.children?.firstIndex(where: { $0 === node }) {
            // Same direction as the run it is in: one more sibling, so the
            // line between them stays draggable on its own.
            let whole = node.size
            node.size = whole * at
            parent.children?.insert(ZoneNode(size: whole * (1 - at)), at: i + 1)
        } else {
            // Across it: the zone becomes a run of two.
            node.columns = columns
            node.children = [ZoneNode(size: at), ZoneNode(size: 1 - at)]
        }

        normalize()
        return true
    }

    /// Takes zone `zone` out, its space going to the neighbour before it
    /// (after it, for the first). The last zone stays.
    @discardableResult
    public func remove(zone: Int) -> Bool {
        guard let hit = find(zone), let parent = hit.parent, var kids = parent.children, kids.count > 1,
              let i = kids.firstIndex(where: { $0 === hit.node }) else { return false }

        let heir = i > 0 ? i - 1 : i + 1
        kids[heir].size += hit.node.size
        kids.remove(at: i)
        parent.children = kids

        normalize()
        return true
    }

    /// Moves a line to `position`, in the same point space it was laid out
    /// in, resizing only the two pieces either side of it and keeping each at
    /// least `minLength` along the run.
    public func moveDivider(_ d: inout ZoneDivider, to position: CGFloat, minLength: CGFloat) {
        // Finite: a NaN would slip past both clamps below and into the shares.
        guard let kids = d.run.children, d.index >= 0, d.index + 1 < kids.count,
              position.isFinite, minLength.isFinite else { return }

        let start = d.vertical ? d.region.minX : d.region.minY
        let extent = d.vertical ? d.region.width : d.region.height
        guard extent > 0 else { return }

        var before = 0.0
        for i in 0..<d.index { before += kids[i].size }

        let pair = kids[d.index].size + kids[d.index + 1].size
        let least = Double(minLength / extent)
        let lo = least, hi = pair - least
        guard lo <= hi else { return }

        var first = Double((position - start) / extent) - before
        if first < lo { first = lo }
        if first > hi { first = hi }

        kids[d.index].size = first
        kids[d.index + 1].size = pair - first
        d.position = start + (CGFloat(before + first) * extent).rounded(.toNearestOrEven)
    }

    /// Zone number `zone` and the run it sits in (nil for a layout of one zone).
    private func find(_ zone: Int) -> (node: ZoneNode, parent: ZoneNode?)? {
        var index = 0
        return ZoneLayout.find(root, nil, zone, &index)
    }

    private static func find(_ n: ZoneNode, _ up: ZoneNode?, _ zone: Int, _ index: inout Int) -> (node: ZoneNode, parent: ZoneNode?)? {
        guard let kids = n.children, !kids.isEmpty else {
            let hit = index == zone
            index += 1
            return hit ? (n, up) : nil
        }
        for c in kids {
            if let hit = find(c, n, zone, &index) { return hit }
        }
        return nil
    }

    /// Folds the tree back into its one canonical shape, so a layout that
    /// looks like a preset also compares equal to it.
    private func normalize() {
        root.size = 1
        ZoneLayout.normalize(root)
    }

    private static func normalize(_ n: ZoneNode) {
        guard let kids = n.children, !kids.isEmpty else {
            n.children = nil
            return
        }

        for c in kids { normalize(c) }

        // A run inside a run going the same way is just more siblings.
        var flat: [ZoneNode] = []
        for c in kids {
            if let grand = c.children, !grand.isEmpty, c.columns == n.columns {
                for g in grand {
                    g.size *= c.size
                    flat.append(g)
                }
            } else {
                flat.append(c)
            }
        }
        n.children = flat

        // A run of one is that one.
        if flat.count == 1 {
            let only = flat[0]
            n.columns = only.columns
            n.children = only.isZone ? nil : only.children
            return
        }

        let sum = flat.reduce(0.0) { $0 + $1.size }
        for c in flat {
            c.size = sum > 0 ? c.size / sum : 1.0 / Double(flat.count)
        }
    }

    // MARK: - Comparing

    /// The same zones in the same places, give or take a rounding.
    public func sameAs(_ other: ZoneLayout) -> Bool {
        ZoneLayout.same(root, other.root)
    }

    private static func same(_ a: ZoneNode, _ b: ZoneNode) -> Bool {
        if abs(a.size - b.size) > 0.002 { return false }
        if a.isZone || b.isZone { return a.isZone && b.isZone }
        guard let ak = a.children, let bk = b.children,
              a.columns == b.columns, ak.count == bk.count else { return false }
        for i in 0..<ak.count {
            if !same(ak[i], bk[i]) { return false }
        }
        return true
    }

    // MARK: - Presets

    /// Names of the ready-made layouts, in the order the settings window shows them.
    public static let presetNames: [String] = [
        "Halves", "Thirds", "Focus", "Two-thirds", "Main + 2", "Grid", "Six", "Rows",
    ]

    /// A fresh copy of ready-made layout `i` (see `presetNames`).
    public static func preset(_ i: Int) -> ZoneLayout {
        switch i {
        case 0:
            return ZoneLayout(root: .even(1, columns: true, count: 2))
        case 1:
            return ZoneLayout(root: .even(1, columns: true, count: 3))
        case 2:
            return ZoneLayout(root: .run(1, columns: true, [
                ZoneNode(size: 0.25), ZoneNode(size: 0.5), ZoneNode(size: 0.25),
            ]))
        case 3:
            return ZoneLayout(root: .run(1, columns: true, [
                ZoneNode(size: 2.0 / 3), ZoneNode(size: 1.0 / 3),
            ]))
        case 4:
            return ZoneLayout(root: .run(1, columns: true, [
                ZoneNode(size: 0.6), .even(0.4, columns: false, count: 2),
            ]))
        case 5:
            return ZoneLayout(root: .run(1, columns: true, [
                .even(0.5, columns: false, count: 2), .even(0.5, columns: false, count: 2),
            ]))
        case 6:
            return ZoneLayout(root: .run(1, columns: true, [
                .even(1.0 / 3, columns: false, count: 2), .even(1.0 / 3, columns: false, count: 2),
                .even(1.0 / 3, columns: false, count: 2),
            ]))
        default:
            return ZoneLayout(root: .even(1, columns: false, count: 2))
        }
    }

    /// The index of the preset this layout matches, or -1 for one of the user's own.
    public func presetIndex() -> Int {
        for i in 0..<ZoneLayout.presetNames.count {
            if sameAs(ZoneLayout.preset(i)) { return i }
        }
        return -1
    }

    /// The layout a display starts with, before anyone has picked one: two
    /// rows on a portrait screen, a wide centre on an ultrawide, halves on
    /// anything else.
    public static func defaultLayout(for size: CGSize) -> ZoneLayout {
        if size.height > size.width { return preset(7) }
        if size.height > 0 && size.width / size.height >= 2.1 { return preset(2) }
        return preset(0)
    }

    // MARK: - Persistence

    /// The tree as JSONSerialization objects, in the same schema the Windows
    /// version writes: `{"size", "columns", "children"}`, the last two only on a run.
    public func toJSON() -> [String: Any] {
        ZoneLayout.nodeToJSON(root)
    }

    private static func nodeToJSON(_ n: ZoneNode) -> [String: Any] {
        // NaN and the infinities are not JSON numbers, and JSONSerialization
        // raises an Objective-C exception on one, taking the app down rather
        // than failing the write. Null costs the one share instead: it reads
        // back as a whole share and normalises away.
        var d: [String: Any] = ["size": n.size.isFinite ? n.size as Any : NSNull()]
        if let kids = n.children, !kids.isEmpty {
            d["columns"] = n.columns
            d["children"] = kids.map { nodeToJSON($0) }
        }
        return d
    }

    /// Reads a layout back. Nil for anything malformed rather than a crash:
    /// one bad layout should cost that display its layout, not every other
    /// setting in the file with it.
    public static func fromJSON(_ v: Any?) -> ZoneLayout? {
        guard let root = nodeFromJSON(v, depth: 0) else { return nil }
        return ZoneLayout(root: root)
    }

    private static func nodeFromJSON(_ v: Any?, depth: Int) -> ZoneNode? {
        guard depth <= 12, let d = v as? [String: Any] else { return nil }

        let size = JSON.number(d["size"]) ?? 1
        guard size.isFinite && size > 0 else { return nil }

        let n = ZoneNode(size: size)
        guard let kids = d["children"] as? [Any], !kids.isEmpty else { return n }

        n.columns = JSON.bool(d["columns"]) ?? true
        var children: [ZoneNode] = []
        children.reserveCapacity(kids.count)
        for k in kids {
            guard let c = nodeFromJSON(k, depth: depth + 1) else { return nil }
            children.append(c)
        }
        n.children = children
        return n
    }
}
