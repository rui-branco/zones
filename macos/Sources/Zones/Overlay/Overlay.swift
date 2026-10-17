import AppKit

/// The zones drawn over a display: while a window is dragged, and for a
/// moment to show a layout (Show on Screen, a size flip).
///
/// CONTRACT: the settings window calls flash; the engine calls show/hide.
///
/// One borderless window per display, made the first time that display
/// needs one and kept: a drag redraws it many times a second, and building
/// a window each time would be the slow part. The mouse goes straight
/// through it and it never activates Zones, so the drag underneath carries
/// on as if it were not there.
final class Overlay {
    static let shared = Overlay()

    /// By display key.
    private var windows: [String: NSWindow] = [:]

    /// The display showing zones now, nil when none is.
    private var shownKey: String?

    /// A flash's hide, still to come.
    private var pendingHide: DispatchWorkItem?

    private init() {}

    /// Draws `zones` (y-down global points) over `display`, those in
    /// `selected` lit, and `label` ("Size 2 of 3") as a pill at the top when
    /// given. Stays up until `hide()`.
    func show(display: Display, zones: [CGRect], selected: Set<Int>, label: String?) {
        pendingHide?.cancel()
        pendingHide = nil

        // A drag crossing to another display takes its zones with it.
        for (key, window) in windows where key != display.key && window.isVisible {
            window.orderOut(nil)
        }

        let window = self.window(for: display)
        let frame = Displays.toCocoa(display.frame)
        if window.frame != frame { window.setFrame(frame, display: false) }

        // The view draws in the display's own points, from its top-left.
        let origin = display.frame.origin
        (window.contentView as? OverlayView)?.content = OverlayView.Content(
            zones: zones.map { $0.offsetBy(dx: -origin.x, dy: -origin.y) },
            selected: selected,
            label: label,
            labelTop: display.visibleFrame.minY - origin.y + 16,
            labelCentre: display.visibleFrame.midX - origin.x)

        if !window.isVisible { window.orderFrontRegardless() }
        shownKey = display.key
    }

    func hide() {
        pendingHide?.cancel()
        pendingHide = nil
        for window in windows.values where window.isVisible {
            window.orderOut(nil)
        }
        shownKey = nil
    }

    var isShowing: Bool { shownKey != nil }

    /// `show` with nothing selected, then `hide` after `duration` seconds.
    func flash(display: Display, zones: [CGRect], label: String?, duration: TimeInterval) {
        show(display: display, zones: zones, selected: [], label: label)

        let hide = DispatchWorkItem { [weak self] in self?.hide() }
        pendingHide = hide
        DispatchQueue.main.asyncAfter(deadline: .now() + duration, execute: hide)
    }

    private func window(for display: Display) -> NSWindow {
        if let window = windows[display.key] { return window }

        let frame = Displays.toCocoa(display.frame)
        let window = NSWindow(contentRect: frame, styleMask: .borderless, backing: .buffered, defer: true)
        window.isReleasedWhenClosed = false
        window.level = .floating
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = false
        window.ignoresMouseEvents = true
        // On every Space, full-screen ones included, and left out of
        // Mission Control and the window cycle: it is not a window to the user.
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        window.contentView = OverlayView(frame: NSRect(origin: .zero, size: frame.size))
        window.setFrame(frame, display: false)

        windows[display.key] = window
        return window
    }
}
