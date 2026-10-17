import AppKit
import CoreGraphics

/// One display, in the y-down global space ZonesCore and the Accessibility
/// API both work in: origin at the top-left of the main display, in points.
/// AppKit's own y-up frames are converted once, here, so nothing else in the
/// app has to remember which way is up.
struct Display: Equatable {
    /// Stable across reboots and reconnects (the display's UUID), unlike the
    /// numeric id: layouts are kept by it.
    let key: String
    let id: CGDirectDisplayID
    /// What macOS calls it, "LG HDR 4K" or "Built-in Retina Display".
    let name: String
    /// The whole display.
    let frame: CGRect
    /// Minus the menu bar and the Dock: where zones go.
    let visibleFrame: CGRect
    let scale: CGFloat
    /// The display with the menu bar, which the y-down space is measured from.
    let isMain: Bool
}

enum Displays {
    /// Every display, left to right, then top to bottom.
    static func all() -> [Display] {
        NSScreen.screens.compactMap(display(for:)).sorted {
            $0.frame.minX != $1.frame.minX ? $0.frame.minX < $1.frame.minX : $0.frame.minY < $1.frame.minY
        }
    }

    /// The display at a y-down point, or the nearest one when the point is
    /// between or beyond them.
    static func at(_ point: CGPoint) -> Display? {
        let displays = all()
        if let hit = displays.first(where: { $0.frame.contains(point) }) { return hit }
        return displays.min { distance(point, $0.frame) < distance(point, $1.frame) }
    }

    static func display(for screen: NSScreen) -> Display? {
        guard let number = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber
        else { return nil }
        let id = CGDirectDisplayID(number.uint32Value)

        var key = "display-\(id)"
        if let uuid = CGDisplayCreateUUIDFromDisplayID(id)?.takeRetainedValue(),
           let text = CFUUIDCreateString(nil, uuid) as String? {
            key = text
        }

        return Display(
            key: key,
            id: id,
            name: screen.localizedName,
            frame: toYDown(screen.frame),
            visibleFrame: toYDown(screen.visibleFrame),
            scale: screen.backingScaleFactor,
            isMain: screen == NSScreen.screens.first)
    }

    /// The NSScreen behind a display, for placing AppKit windows on it.
    static func screen(for display: Display) -> NSScreen? {
        NSScreen.screens.first {
            ($0.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value == display.id
        }
    }

    /// AppKit (y-up from the main display's bottom) to y-down.
    static func toYDown(_ r: CGRect) -> CGRect {
        CGRect(x: r.minX, y: mainHeight - r.maxY, width: r.width, height: r.height)
    }

    /// Y-down to AppKit, for NSWindow frames. The same flip, undone.
    static func toCocoa(_ r: CGRect) -> CGRect {
        CGRect(x: r.minX, y: mainHeight - r.maxY, width: r.width, height: r.height)
    }

    /// The main display's height: the line the two spaces flip about.
    private static var mainHeight: CGFloat {
        NSScreen.screens.first?.frame.height ?? 0
    }

    private static func distance(_ p: CGPoint, _ r: CGRect) -> CGFloat {
        let dx = max(r.minX - p.x, 0, p.x - r.maxX)
        let dy = max(r.minY - p.y, 0, p.y - r.maxY)
        return dx * dx + dy * dy
    }
}
