import CoreGraphics

/// Where Zones last put one window, and what it was like before.
struct Placement {
    /// The window's size before it first went into a zone: what dragging it
    /// back out of one gives back.
    var original: CGSize

    /// The size the zone gave it. Any other size means something has resized
    /// it since, and the size is no longer ours to give back.
    var snapped: CGSize

    /// The display whose layout the zones belong to.
    var displayKey: String

    /// The zones it spans, in zone order: one from the shortcut, one or more
    /// from a drag that let go near the line between two.
    var indices: [Int]

    /// Which of the display's sizes those zones were in.
    var sizeIndex: Int

    /// Where the window was left, as read back after placing. Anywhere else
    /// means it has been moved since.
    var frame: CGRect
}

/// The windows Zones has put in a zone, so that dragging one back out can
/// give it its own size again, the shortcut can tell which zone a window
/// too big to fill its zone is in, and a size flip knows which windows to
/// take along. One record for all three, where the Windows version keeps
/// two: here a window is the same AX reference whichever way it was found.
enum Placements {
    private(set) static var all: [WindowRef: Placement] = [:]

    static func of(_ window: WindowRef) -> Placement? {
        all[window]
    }

    static func remove(_ window: WindowRef) {
        all[window] = nil
    }

    /// Fits `window` to `target` (y-down global points) and records it.
    /// Moving from one zone to another, the size to give back is still the
    /// one it had before the first zone, not the first zone's.
    static func place(_ window: WindowRef, _ target: CGRect, displayKey: String, indices: [Int], sizeIndex: Int) {
        let before = window.frame
        var original = before?.size ?? target.size
        if let record = all[window], let size = before?.size, size.matches(record.snapped, within: 1) {
            original = record.original
        }

        window.setFrame(target)

        // What the app actually did: one with a minimum size bigger than
        // the zone, or one that sizes in steps (a terminal), stops short.
        let frame = window.frame ?? target
        all[window] = Placement(original: original, snapped: frame.size, displayKey: displayKey,
                                indices: indices, sizeIndex: sizeIndex, frame: frame)

        all = all.filter { $0.key.isValid }
    }
}

extension CGRect {
    /// The same rectangle, give or take `tolerance` points on every edge.
    func matches(_ other: CGRect, within tolerance: CGFloat) -> Bool {
        abs(minX - other.minX) <= tolerance && abs(minY - other.minY) <= tolerance
            && abs(maxX - other.maxX) <= tolerance && abs(maxY - other.maxY) <= tolerance
    }
}

extension CGSize {
    func matches(_ other: CGSize, within tolerance: CGFloat) -> Bool {
        abs(width - other.width) <= tolerance && abs(height - other.height) <= tolerance
    }
}
