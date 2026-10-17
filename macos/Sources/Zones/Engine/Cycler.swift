import CoreGraphics
import ZonesCore

/// The zone shortcut: each press moves the focused window `step` zones on,
/// +1 or -1, round its display's layout. One not in a zone yet is put in
/// the one it is over: the first press snaps it where it is, every press
/// after moves it on.
enum Cycler {
    static func cycle(_ step: Int) {
        let config = AppState.shared.config
        guard config.snapEnabled else { return }

        // A window being dragged is the drag's to place. A minimized one is
        // out of the way on purpose, a full-screen one has a Space of its
        // own, and a fixed-size one cannot be made to fit.
        guard let window = WindowRef.focused(),
              window != DragSnapper.shared.draggedWindow,
              !window.isMinimized, !window.isFullScreen, window.isResizable,
              let frame = window.frame else { return }

        let centre = CGPoint(x: frame.midX, y: frame.midY)
        guard let display = Displays.at(centre) else { return }

        let layouts = config.layouts(for: display.key, size: display.visibleFrame.size)
        let zones = layouts.current.zonesIn(display.visibleFrame, spacing: CGFloat(config.spacing))
        let n = zones.count
        guard n > 0 else { return }

        // Which zone it is in now. The one Zones last put it in, while it is
        // still where it was left there: the only way to tell for a window
        // too big to fill its zone. Otherwise the one its frame matches, to a
        // point or two: put there some other way, or moved away and back.
        var current = -1
        if let placed = Placements.of(window), placed.displayKey == display.key,
           placed.frame.matches(frame, within: 1),
           let first = placed.indices.first, first >= 0, first < n {
            current = first
        }
        if current < 0, let i = zones.firstIndex(where: { $0.matches(frame, within: 2) }) {
            current = i
        }

        // In none of them, it goes into the one its middle is over, or with
        // that in a gap or the margin, the nearest.
        let next: Int
        if current >= 0 {
            next = ((current + step) % n + n) % n
        } else if let over = zones.firstIndex(where: { $0.contains(centre) }) {
            next = over
        } else if let nearest = DragSnapper.nearest(centre, zones) {
            next = nearest
        } else {
            return
        }

        Placements.place(window, zones[next], displayKey: display.key, indices: [next], sizeIndex: layouts.active)
    }
}
