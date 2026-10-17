import AppKit
import ZonesCore

/// Window snapping by drag: while a window is dragged by its title bar with
/// Shift held (or without, as the settings have it), the zones of the
/// display under the cursor show over everything, the ones under the cursor
/// lit, and on the drop the window is fitted to them.
///
/// macOS has no event for a window starting to move, so Zones watches the
/// mouse itself through an event tap: a press notes the window under it,
/// the drag that follows is a move if the window's origin follows it and its
/// size does not, and the release ends it. The tap sits in the path of every
/// click on the machine, so its callback only notes what happened and
/// returns; anything that has to ask a window (an Accessibility call) runs
/// just after, on the main queue. A callback that takes too long gets the
/// tap switched off by macOS.
///
/// While the zones show, the scroll wheel picks which of the display's
/// sizes to drop into; those scroll events are taken, so the window being
/// dragged does not scroll as well.
final class DragSnapper {
    static let shared = DragSnapper()

    /// How far past its edge a zone still counts as under the cursor, in
    /// points, on top of half the gap. Enough that near the line between two
    /// zones both are lit, and the window spans the pair.
    private static let reach: CGFloat = 12

    /// The fewest seconds between two reads of the window's frame.
    private static let readInterval: TimeInterval = 0.03

    /// The fewest seconds between two size steps by scrolling, so a
    /// trackpad's momentum does not race through every size.
    private static let scrollInterval: TimeInterval = 0.15

    private var tap: CFMachPort?

    // The drag being followed. `window` is nil between drags.
    private var window: WindowRef?
    private var startFrame = CGRect.zero
    private var lastFrame = CGRect.zero
    private var lastRead: TimeInterval = 0
    private var sizeChanges = 0
    private var moving = false
    private var resizing = false

    // Noted by the tap callback, used by the work queued after it.
    private var cursor = CGPoint.zero
    private var flags: CGEventFlags = []
    private var updateQueued = false
    private var rereadQueued = false
    private var lastScrollStep: TimeInterval = 0

    // What the overlay shows, and where the window would go if dropped now.
    private var showing = false
    private var display: Display?
    private var previewSize = 0
    private var sizeCount = 1
    private var zones: [CGRect] = []
    private var selected: [Int] = []
    private var target = CGRect.zero

    private init() {}

    /// The window being dragged right now, if any: the shortcut leaves it to the drag.
    var draggedWindow: WindowRef? { window }

    /// Starts watching the mouse. Needs Accessibility: macOS will not hand
    /// out a tap that can take events before Zones is allowed. Once; later
    /// calls do nothing.
    func start() {
        guard tap == nil else { return }

        let types: [CGEventType] = [.leftMouseDown, .leftMouseDragged, .leftMouseUp, .scrollWheel, .flagsChanged]
        let mask = types.reduce(CGEventMask(0)) { $0 | (CGEventMask(1) << $1.rawValue) }

        guard let tap = CGEvent.tapCreate(
            tap: .cgSessionEventTap, place: .headInsertEventTap, options: .defaultTap,
            eventsOfInterest: mask,
            callback: { _, type, event, _ in DragSnapper.shared.handle(type, event) },
            userInfo: nil),
            let source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        else {
            NSLog("Zones: could not create the mouse event tap")
            return
        }

        self.tap = tap
        // Common modes, so drags are still seen while a menu is tracking.
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
    }

    // MARK: - The tap

    /// Every event of interest on the machine comes through here, on the
    /// main thread. Cheap on purpose: note, queue, return.
    private func handle(_ type: CGEventType, _ event: CGEvent) -> Unmanaged<CGEvent>? {
        switch type {
        case .tapDisabledByTimeout, .tapDisabledByUserInput:
            if let tap = tap { CGEvent.tapEnable(tap: tap, enable: true) }

        case .leftMouseDown:
            let point = event.location
            DispatchQueue.main.async { self.mouseDown(at: point) }

        case .leftMouseDragged:
            cursor = event.location
            flags = event.flags
            queueUpdate()

        case .flagsChanged:
            // Shift pressed or let go mid-drag, with the mouse standing still.
            flags = event.flags
            if window != nil { queueUpdate() }

        case .leftMouseUp:
            let point = event.location
            DispatchQueue.main.async { self.mouseUp(at: point) }

        case .scrollWheel:
            if showing && sizeCount >= 2 {
                scroll(event)
                return nil
            }

        default:
            break
        }
        return Unmanaged.passUnretained(event)
    }

    /// One update for however many drag events arrived since the last one.
    private func queueUpdate() {
        guard !updateQueued else { return }
        updateQueued = true
        DispatchQueue.main.async {
            self.updateQueued = false
            self.update()
        }
    }

    /// Wheel down (towards you) is the next size, up the one before, by the
    /// way the wheel actually turned: undoing natural scrolling, which
    /// macOS has already applied to the delta.
    private func scroll(_ event: CGEvent) {
        let now = ProcessInfo.processInfo.systemUptime
        guard now - lastScrollStep >= DragSnapper.scrollInterval, let ns = NSEvent(cgEvent: event) else { return }
        let dy = ns.isDirectionInvertedFromDevice ? -ns.scrollingDeltaY : ns.scrollingDeltaY
        guard dy != 0 else { return }

        lastScrollStep = now
        let step = dy < 0 ? 1 : -1
        DispatchQueue.main.async { self.stepPreview(step) }
    }

    // MARK: - The drag

    private func mouseDown(at point: CGPoint) {
        // A release that never came (the tap off for a moment) leaves
        // nothing behind for this press to trip over.
        endDrag()
        guard AppState.shared.config.snapEnabled,
              let window = WindowRef.at(point),
              !window.isFullScreen, window.isResizable,
              let frame = window.frame else { return }

        self.window = window
        startFrame = frame
        lastFrame = frame
        lastRead = ProcessInfo.processInfo.systemUptime
        sizeChanges = 0
        moving = false
        resizing = false
        display = nil
    }

    /// While the button is down: whether the window is moving, whether zones
    /// are wanted, and which of them are under the cursor.
    private func update() {
        guard let window = window else { return }

        // The frame is the one thing here that asks the window's app, so it
        // is read at most every 30 ms; one more read is queued for the end of
        // the wait, so a drag that stops dead is still seen to have moved.
        let now = ProcessInfo.processInfo.systemUptime
        let wait = DragSnapper.readInterval - (now - lastRead)
        if wait <= 0 {
            lastRead = now
            guard let frame = window.frame else {
                // Gone, or its app not answering: an app that does not answer
                // now will not answer the next read either, and each one
                // would hold up every mouse event on the machine meanwhile.
                endDrag()
                return
            }
            observe(frame)
        } else if !rereadQueued {
            rereadQueued = true
            DispatchQueue.main.asyncAfter(deadline: .now() + wait) {
                self.rereadQueued = false
                self.update()
            }
        }

        guard moving, !resizing else {
            hideOverlay()
            return
        }

        // Read afresh every time: the settings window changes them live.
        let config = AppState.shared.config
        let shift = flags.contains(.maskShift)
        guard config.snapEnabled, config.holdShift ? shift : !shift else {
            hideOverlay()
            return
        }

        aim(at: cursor, draw: true)
    }

    /// A move shifts the window's origin and keeps its size. A tiled window
    /// may change size once, as macOS gives it its own size back when the
    /// drag begins; a resize goes on changing it, and is left alone.
    private func observe(_ frame: CGRect) {
        if !frame.size.matches(lastFrame.size, within: 0.5) {
            sizeChanges += 1
            if sizeChanges >= 2 { resizing = true }
        } else if abs(frame.minX - lastFrame.minX) > 0.5 || abs(frame.minY - lastFrame.minY) > 0.5 {
            moving = true
        }
        lastFrame = frame
    }

    /// Works out which zones are under `point` and where the window would
    /// land, and with `draw` brings the overlay up to date. False when there
    /// is no display to aim at.
    @discardableResult
    private func aim(at point: CGPoint, draw: Bool) -> Bool {
        guard let display = Displays.at(point) else {
            hideOverlay()
            return false
        }

        let config = AppState.shared.config
        let layouts = config.layouts(for: display.key, size: display.visibleFrame.size)

        // Each display starts at the size it is using; scrolling on one
        // display says nothing about the next.
        if display.key != self.display?.key { previewSize = layouts.active }
        self.display = display
        sizeCount = layouts.sizes.count
        previewSize = min(max(previewSize, 0), sizeCount - 1)

        let spacing = CGFloat(config.spacing)
        zones = layouts.sizes[previewSize].zonesIn(display.visibleFrame, spacing: spacing)
        selected = DragSnapper.zonesUnder(point, zones, reach: spacing / 2 + DragSnapper.reach)
        target = selected.dropFirst().reduce(selected.first.map { zones[$0] } ?? .zero) { $0.union(zones[$1]) }

        if draw {
            let label = sizeCount >= 2 ? "Size \(previewSize + 1) of \(sizeCount)  ·  scroll to change" : nil
            Overlay.shared.show(display: display, zones: zones, selected: Set(selected), label: label)
            showing = true
        }
        return true
    }

    private func stepPreview(_ step: Int) {
        guard window != nil, showing, sizeCount >= 2 else { return }
        previewSize = ((previewSize + step) % sizeCount + sizeCount) % sizeCount
        aim(at: cursor, draw: true)
    }

    /// Ends the drag: a move that was showing zones fits the window to the
    /// lit ones; a move that was not gives a window taken out of a zone its
    /// own size back. A plain click, which moves nothing, does neither.
    private func mouseUp(at point: CGPoint) {
        guard let window = window else { return }

        // One last look: a quick flick can be over before any read saw it move.
        if let frame = window.frame { observe(frame) }
        let moved = moving && !resizing && !lastFrame.matches(startFrame, within: 0.5)

        // Aimed again at where the button came up, not where the last update
        // saw the cursor: a flick that lets go straight away would otherwise
        // land one zone short.
        var snap = moved && showing
        if snap { snap = aim(at: point, draw: false) && !selected.isEmpty }

        let target = self.target
        let indices = selected
        let display = self.display
        let preview = previewSize
        endDrag()

        if snap, let display = display {
            // A moment for the app to finish its own end of the drag, which
            // could otherwise put the window back where the mouse left it.
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.05) {
                let layouts = AppState.shared.config.layouts(for: display.key, size: display.visibleFrame.size)
                if preview != layouts.active {
                    Engine.activateSize(preview, on: display, flash: false)
                }
                Placements.place(window, target, displayKey: display.key, indices: indices, sizeIndex: preview)
            }
        } else if moved {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.05) {
                DragSnapper.restoreSize(window, cursor: point)
            }
        }
    }

    private func endDrag() {
        window = nil
        hideOverlay()
        display = nil
        moving = false
        resizing = false
    }

    private func hideOverlay() {
        if showing {
            Overlay.shared.hide()
            showing = false
        }
        selected = []
    }

    /// Gives a window dragged out of a zone, and not into another, the size
    /// it had before it went in. The cursor keeps its place across the
    /// window, so the title bar does not jump out from under it.
    private static func restoreSize(_ window: WindowRef, cursor: CGPoint) {
        guard let record = Placements.of(window) else { return }

        // Moved by hand, it is out of its zone either way. The size is only
        // ours while it is exactly the zone's: anything else (macOS's own
        // tiling, a resize by hand) has taken it over since.
        Placements.remove(window)
        guard let current = window.frame, current.size.matches(record.snapped, within: 1) else { return }

        let size = record.original
        let fx = (cursor.x - current.minX) / max(1, current.width)
        var left = (cursor.x - fx * size.width).rounded()
        var top = current.minY

        // Kept on the display it was dropped on, where it fits there.
        if let work = Displays.at(cursor)?.visibleFrame {
            if size.width <= work.width { left = max(work.minX, min(left, work.maxX - size.width)) }
            if size.height <= work.height { top = max(work.minY, min(top, work.maxY - size.height)) }
        }

        window.setFrame(CGRect(x: left, y: top, width: size.width, height: size.height))
    }

    // MARK: - Geometry

    /// The zones under `point`, in zone order: every one within `reach` of
    /// it, so near the line between two the window spans both; or, with the
    /// point past the edge of them all (in the margin, over the Dock), the
    /// nearest one.
    static func zonesUnder(_ point: CGPoint, _ zones: [CGRect], reach: CGFloat) -> [Int] {
        let hits = zones.indices.filter { zones[$0].insetBy(dx: -reach, dy: -reach).contains(point) }
        if !hits.isEmpty { return hits }
        return nearest(point, zones).map { [$0] } ?? []
    }

    /// The zone nearest to `p`, measured to its edge, not its middle, so a
    /// big zone is not passed over for a small one beside it. Nil when there
    /// are none.
    static func nearest(_ p: CGPoint, _ zones: [CGRect]) -> Int? {
        var nearest: Int?
        var best = CGFloat.greatestFiniteMagnitude
        for (i, z) in zones.enumerated() {
            let dx = max(0, z.minX - p.x, p.x - z.maxX)
            let dy = max(0, z.minY - p.y, p.y - z.maxY)
            let distance = dx * dx + dy * dy
            if distance < best {
                best = distance
                nearest = i
            }
        }
        return nearest
    }
}
