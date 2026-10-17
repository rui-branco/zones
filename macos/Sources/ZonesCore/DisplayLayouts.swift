import Foundation

/// One display's layouts: one or more *sizes* of it (for example the middle
/// zone at 50%, 60%, 70%), one of them active.
/// The user flips between sizes with a shortcut; snapped windows follow.
public struct DisplayLayouts {
    /// Never empty, so there is always a current layout to snap to: emptied,
    /// it goes back to a single Halves.
    public var sizes: [ZoneLayout] {
        didSet {
            if sizes.isEmpty { sizes = [ZoneLayout.preset(0)] }
        }
    }

    /// Which of `sizes` is in use. Kept as given and clamped on read, so
    /// taking sizes away can never leave it pointing past the end.
    public var active: Int {
        get { min(max(storedActive, 0), sizes.count - 1) }
        set { storedActive = newValue }
    }

    private var storedActive: Int

    public var current: ZoneLayout {
        sizes[active]
    }

    public init(_ layout: ZoneLayout) {
        self.init(sizes: [layout], active: 0)
    }

    public init(sizes: [ZoneLayout], active: Int) {
        self.sizes = sizes.isEmpty ? [ZoneLayout.preset(0)] : sizes
        self.storedActive = active
    }

    public func toJSON() -> [String: Any] {
        ["active": active, "sizes": sizes.map { $0.toJSON() }]
    }

    /// Reads one display's layouts back; nil for anything malformed, a bad
    /// size included, so it costs this display and nothing else.
    ///
    /// Also takes a bare layout object (has "size", no "sizes") as a single
    /// size: what a display's entry was before it could have more than one.
    public static func fromJSON(_ v: Any?) -> DisplayLayouts? {
        guard let d = v as? [String: Any] else { return nil }

        if d["sizes"] == nil && d["size"] != nil {
            guard let layout = ZoneLayout.fromJSON(d) else { return nil }
            return DisplayLayouts(layout)
        }

        guard let list = d["sizes"] as? [Any], !list.isEmpty else { return nil }
        var sizes: [ZoneLayout] = []
        sizes.reserveCapacity(list.count)
        for item in list {
            guard let layout = ZoneLayout.fromJSON(item) else { return nil }
            sizes.append(layout)
        }
        return DisplayLayouts(sizes: sizes, active: JSON.int(d["active"]) ?? 0)
    }
}
