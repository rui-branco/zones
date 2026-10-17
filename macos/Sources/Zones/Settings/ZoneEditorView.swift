import AppKit
import SwiftUI
import ZonesCore

/// The canvas where a display's zones are drawn by hand: a scale model of the
/// display, its zones laid out on it as they will be on screen, gaps and all.
/// A port of the Windows ZoneEditor, behaviour for behaviour.
///
/// Everything is direct manipulation, because a layout is easier to see than
/// to describe: a click cuts the zone under the pointer along the guide that
/// follows it, a drag moves a line between zones, a right-click offers the
/// rest. Both the guide and a dragged line are pulled onto halves, thirds and
/// quarters, which are what people almost always want and are hard to hit by
/// hand; ⌥ lets a dragged line go anywhere.
///
/// Each zone shows its real size in points on the display, which is the
/// feedback while a line is dragged: the numbers change as it moves. The
/// layout is edited in place; `onCommit` runs once per finished edit, not on
/// every mouse move, so whoever saves it saves it once.
///
/// Flipped, so its coordinates run top-down like the layout's own.
final class ZoneEditorView: NSView {
    /// Where a dragged line catches, as shares of the whole display.
    private static let lineStops: [CGFloat] = [1.0 / 4, 1.0 / 3, 1.0 / 2, 2.0 / 3, 3.0 / 4]

    /// Where the split guide catches, as shares of its zone. Earlier wins.
    private static let guideStops: [CGFloat] = [1.0 / 2, 1.0 / 3, 2.0 / 3, 1.0 / 4, 3.0 / 4]

    /// How close the guide or a line has to come to a stop to catch on it.
    private static let snapReach: CGFloat = 6

    /// How far either side of a line still grabs it.
    private static let grabReach: CGFloat = 5

    /// How far a press may wander and still be a click.
    private static let clickSlop: CGFloat = 4

    /// The smallest a zone may be cut or dragged to, as a share of the
    /// display: anything thinner is a sliver no window fits.
    private static let minShare: CGFloat = 0.05

    var onCommit: (() -> Void)?

    private var layout: ZoneLayout?
    private var displaySize = CGSize.zero   // the display's visible frame, in its points
    private var spacing: CGFloat = 0        // the real gap on the display, in points

    private var hoverZone = -1
    private var hoverDivider: ZoneDivider?
    private var drag: ZoneDivider?
    private var mouse = CGPoint.zero
    private var shift = false               // the guide runs across, to cut top from bottom

    private var down = false                // the button went down on a zone, not a line
    private var downAt = CGPoint.zero
    private var downZone = -1
    private var menuZone = -1

    private var tracking: NSTrackingArea?
    private var flagsMonitor: Any?

    /// A change of layout being shown as a movement rather than a jump: a
    /// preset picked, another size or display, a split, a removal.
    private struct Morph {
        let start: CFTimeInterval
        /// Where the model display was: it moves too, to another display's shape.
        let fromCanvas: CGRect
        /// For each zone now, where it comes from; nil for one fading in.
        let sources: [CGRect?]
        /// Zones that are gone, fading out where they were.
        let leaving: [CGRect]
    }

    private static let morphDuration: CFTimeInterval = 0.25
    private var morph: Morph?
    private var morphTimer: Timer?

    override var isFlipped: Bool {
        true
    }

    /// Edits `layout` in place, modelled on a display whose visible frame is
    /// `displaySize`, with zones `spacing` real points apart. What was
    /// hovered or dragged is forgotten only for a different layout: SwiftUI
    /// hands the same one back on every update. A different layout morphs
    /// in from the one shown.
    func setLayout(_ layout: ZoneLayout, displaySize: CGSize, spacing: CGFloat) {
        let changed = layout !== self.layout
        let before = changed && self.layout != nil ? shown() : nil
        if changed {
            hoverZone = -1
            hoverDivider = nil
            drag = nil
            down = false
            downZone = -1
        }
        self.layout = layout
        self.displaySize = displaySize
        self.spacing = spacing
        if changed { startMorph(from: before) }
        needsDisplay = true
        window?.invalidateCursorRects(for: self)
    }

    private var ready: Bool {
        layout != nil && displaySize.width > 0 && displaySize.height > 0
    }

    /// The model display: as large as fits, in the display's own
    /// proportions, centred, on whole points like everything laid out on it.
    private func canvas() -> CGRect {
        let area = bounds.insetBy(dx: 2, dy: 2)
        guard ready, area.width > 0, area.height > 0 else { return area }

        let k = min(area.width / displaySize.width, area.height / displaySize.height)
        let w = (displaySize.width * k).rounded()
        let h = (displaySize.height * k).rounded()
        return CGRect(x: (area.minX + (area.width - w) / 2).rounded(.down),
                      y: (area.minY + (area.height - h) / 2).rounded(.down),
                      width: w, height: h)
    }

    /// The real gap at the model's scale, but never so thin the zones run together.
    private func gap(_ canvas: CGRect) -> CGFloat {
        max(4, (spacing * canvas.width / displaySize.width).rounded())
    }

    /// The line whose grab area holds `p`; where two meet, the nearer.
    private func divider(at p: CGPoint, in canvas: CGRect) -> ZoneDivider? {
        guard let layout else { return nil }
        var best: ZoneDivider?
        var bestDistance = CGFloat.infinity
        for d in layout.dividers(in: canvas) {
            guard ZoneEditorView.grabArea(d).contains(p) else { continue }
            let distance = abs((d.vertical ? p.x : p.y) - d.position)
            if distance < bestDistance {
                best = d
                bestDistance = distance
            }
        }
        return best
    }

    private static func grabArea(_ d: ZoneDivider) -> CGRect {
        d.vertical ? d.line.insetBy(dx: -grabReach, dy: 0) : d.line.insetBy(dx: 0, dy: -grabReach)
    }

    private static func zone(at p: CGPoint, in zones: [CGRect]) -> Int {
        zones.firstIndex { $0.contains(p) } ?? -1
    }

    /// Where a click at `p` would cut `zone` (its un-gapped rectangle): an x
    /// for a vertical cut, a y for a horizontal one, caught by the zone's
    /// halves, thirds and quarters.
    private func guide(in zone: CGRect, at p: CGPoint, across: Bool) -> CGFloat {
        let start = across ? zone.minY : zone.minX
        let extent = across ? zone.height : zone.width
        let pos = (across ? p.y : p.x).rounded()
        for f in ZoneEditorView.guideStops {
            let c = start + (extent * f).rounded()
            if abs(pos - c) <= ZoneEditorView.snapReach { return c }
        }
        return pos
    }

    /// Works out what is under the pointer.
    private func hover(_ p: CGPoint) {
        mouse = p
        guard ready, let layout else { return }
        let canvas = canvas()
        hoverDivider = divider(at: p, in: canvas)
        hoverZone = hoverDivider == nil ? ZoneEditorView.zone(at: p, in: layout.zonesIn(canvas, spacing: 0)) : -1
    }

    /// After an edit: redraw, and tell the owner.
    private func edited() {
        needsDisplay = true
        window?.invalidateCursorRects(for: self)
        onCommit?()
    }

    /// A split or a removal from the menu: morphed, and committed.
    private func edit(_ change: (ZoneLayout) -> Bool) {
        guard let layout else { return }
        let before = shown()
        guard change(layout) else { return }
        hoverZone = -1
        hoverDivider = nil
        startMorph(from: before)
        edited()
    }

    // MARK: - Morphing

    /// The model display and its zones as they are on screen right now,
    /// part way through a morph included: where the next morph starts from.
    private func shown() -> (canvas: CGRect, zones: [CGRect])? {
        guard ready, let layout else { return nil }
        let canvas = canvas()
        guard canvas.width > 0, canvas.height > 0 else { return nil }
        let zones = layout.zonesIn(canvas, spacing: gap(canvas))
        guard let morph, morph.sources.count == zones.count else { return (canvas, zones) }
        let t = morphProgress(morph)
        return (ZoneEditorView.lerp(morph.fromCanvas, canvas, t),
                zones.enumerated().map { i, zone in morph.sources[i].map { ZoneEditorView.lerp($0, zone, t) } ?? zone })
    }

    /// Starts moving from `before` to the layout as it is now. Each old zone
    /// goes to the new zone it overlaps most, one to one, the biggest
    /// overlaps first, so a split zone shrinks into one half while the other
    /// half fades in, and a removed one fades out under its growing neighbour.
    private func startMorph(from before: (canvas: CGRect, zones: [CGRect])?) {
        guard let before, window != nil, !Motion.reduced, ready, let layout else {
            stopMorph()
            return
        }
        let canvas = canvas()
        guard canvas.width > 0, canvas.height > 0 else { return }
        let zones = layout.zonesIn(canvas, spacing: gap(canvas))
        if before.canvas == canvas && before.zones == zones {
            stopMorph()
            return
        }

        var pairs: [(overlap: CGFloat, old: Int, new: Int)] = []
        for (i, old) in before.zones.enumerated() {
            for (j, new) in zones.enumerated() {
                let common = old.intersection(new)
                if !common.isNull && common.width > 0 && common.height > 0 {
                    pairs.append((common.width * common.height, i, j))
                }
            }
        }
        pairs.sort { $0.overlap > $1.overlap }

        var sources = [CGRect?](repeating: nil, count: zones.count)
        var taken = Set<Int>()
        for pair in pairs where sources[pair.new] == nil && !taken.contains(pair.old) {
            sources[pair.new] = before.zones[pair.old]
            taken.insert(pair.old)
        }
        let leaving = before.zones.indices.filter { !taken.contains($0) }.map { before.zones[$0] }

        morph = Morph(start: CACurrentMediaTime(), fromCanvas: before.canvas, sources: sources, leaving: leaving)
        if morphTimer == nil {
            let timer = Timer(timeInterval: 1.0 / 120, target: self, selector: #selector(morphTick),
                              userInfo: nil, repeats: true)
            // Common modes: on through a menu closing or a resize.
            RunLoop.main.add(timer, forMode: .common)
            morphTimer = timer
        }
    }

    @objc private func morphTick() {
        needsDisplay = true
        if let morph, CACurrentMediaTime() - morph.start >= ZoneEditorView.morphDuration {
            stopMorph()
        }
    }

    private func stopMorph() {
        morphTimer?.invalidate()
        morphTimer = nil
        morph = nil
        needsDisplay = true
    }

    /// How far along `morph` is, eased out: quick to leave, gentle to land.
    private func morphProgress(_ morph: Morph) -> CGFloat {
        let p = min(max((CACurrentMediaTime() - morph.start) / ZoneEditorView.morphDuration, 0), 1)
        return CGFloat(1 - pow(1 - p, 3))
    }

    private static func lerp(_ a: CGRect, _ b: CGRect, _ t: CGFloat) -> CGRect {
        CGRect(x: a.minX + (b.minX - a.minX) * t, y: a.minY + (b.minY - a.minY) * t,
               width: a.width + (b.width - a.width) * t, height: a.height + (b.height - a.height) * t)
    }

    // MARK: - Painting

    override func draw(_ dirtyRect: NSRect) {
        guard ready, let layout else { return }
        let canvas = canvas()
        guard canvas.width > 0, canvas.height > 0 else { return }
        let gap = gap(canvas)

        let zones = layout.zonesIn(canvas, spacing: gap)
        let real = layout.zonesIn(CGRect(origin: .zero, size: displaySize), spacing: spacing)
        let moving = morph.flatMap { $0.sources.count == zones.count ? $0 : nil }
        let t = moving.map { morphProgress($0) } ?? 1

        // The screen: a shade off the card it sits on, sunk into it.
        Palette.plate.setFill()
        let plate = moving.map { ZoneEditorView.lerp($0.fromCanvas, canvas, t) } ?? canvas
        NSBezierPath(roundedRect: plate, xRadius: 8, yRadius: 8).fill()

        if let moving {
            // Mid-morph, no hover, guide or line: they belong to where the
            // zones are going, not where they are.
            for zone in moving.leaving {
                drawZone(zone, number: nil, real: .zero, hot: false, alpha: 1 - t)
            }
            for (i, zone) in zones.enumerated() {
                let from = moving.sources[i]
                drawZone(from.map { ZoneEditorView.lerp($0, zone, t) } ?? zone, number: i + 1,
                         real: i < real.count ? real[i] : zone, hot: false, alpha: from == nil ? t : 1)
            }
            return
        }

        let line = drag ?? hoverDivider
        let hot = line == nil && hoverZone < zones.count ? hoverZone : -1

        for (i, zone) in zones.enumerated() {
            drawZone(zone, number: i + 1, real: i < real.count ? real[i] : zone, hot: i == hot)
        }

        if let line {
            drawDivider(line, canvas: canvas, gap: gap)
        } else if hot >= 0 {
            let raw = layout.zonesIn(canvas, spacing: 0)
            if hot < raw.count { drawGuide(raw[hot], zone: zones[hot]) }
        }
    }

    /// One zone: `number` nil for one on its way out, `alpha` under 1 while
    /// it fades in or out.
    private func drawZone(_ zone: CGRect, number: Int?, real: CGRect, hot: Bool, alpha: CGFloat = 1) {
        let context = NSGraphicsContext.current?.cgContext
        if alpha < 1 {
            // As one layer, so the edge does not show through the fill.
            context?.saveGState()
            context?.setAlpha(max(alpha, 0))
            context?.beginTransparencyLayer(auxiliaryInfo: nil)
        }
        defer {
            if alpha < 1 {
                context?.endTransparencyLayer()
                context?.restoreGState()
            }
        }

        let stroke: CGFloat = hot ? 1.5 : 1
        let path = NSBezierPath(roundedRect: zone.insetBy(dx: stroke / 2, dy: stroke / 2), xRadius: 6, yRadius: 6)

        // Raised off the screen: the window's own colour, lighter than it.
        NSColor.windowBackgroundColor.setFill()
        path.fill()
        if hot {
            Palette.accentTint.setFill()
            path.fill()
        }
        (hot ? NSColor.controlAccentColor : NSColor.separatorColor).setStroke()
        path.lineWidth = stroke
        path.stroke()

        // The number, as big as the zone comfortably takes, and the real size
        // under it when there is room for a second line. Too small for a
        // legible number, a sliver of a zone shows neither.
        let px = min(26, zone.height * 0.35)
        guard let number, px >= 8 else { return }

        let numberText = NSAttributedString(string: "\(number)", attributes: [
            .font: NSFont.systemFont(ofSize: px, weight: .semibold),
            .foregroundColor: NSColor.secondaryLabelColor,
        ])
        let sizeText = NSAttributedString(string: "\(Int(real.width)) × \(Int(real.height))", attributes: [
            .font: NSFont.systemFont(ofSize: 11),
            .foregroundColor: NSColor.tertiaryLabelColor,
        ])
        let numberSize = numberText.size()
        let sizeSize = sizeText.size()
        let sizeLine = zone.height >= 60 && sizeSize.width <= zone.width - 8

        let top = zone.minY + (zone.height - numberSize.height - (sizeLine ? sizeSize.height : 0)) / 2
        numberText.draw(at: CGPoint(x: zone.midX - numberSize.width / 2, y: top))
        if sizeLine {
            sizeText.draw(at: CGPoint(x: zone.midX - sizeSize.width / 2, y: top + numberSize.height))
        }
    }

    /// The line under the pointer, or being dragged, in the middle of the gap
    /// it sits in, from where the zones beside it start to where they end.
    private func drawDivider(_ d: ZoneDivider, canvas: CGRect, gap: CGFloat) {
        // zonesIn gives an inner edge half the gap each side, the odd point
        // to the left or top, and an edge on the canvas's own edge all of it.
        let inner2 = (gap / 2).rounded(.down)
        let inner1 = gap - inner2
        let at = d.position - inner1 + gap / 2

        let r = d.region
        let from: CGFloat
        let to: CGFloat
        if d.vertical {
            from = r.minY + (r.minY <= canvas.minY ? gap : inner2)
            to = r.maxY - (r.maxY >= canvas.maxY ? gap : inner1)
        } else {
            from = r.minX + (r.minX <= canvas.minX ? gap : inner2)
            to = r.maxX - (r.maxX >= canvas.maxX ? gap : inner1)
        }

        let path = NSBezierPath()
        if d.vertical {
            path.move(to: CGPoint(x: at, y: from))
            path.line(to: CGPoint(x: at, y: to))
        } else {
            path.move(to: CGPoint(x: from, y: at))
            path.line(to: CGPoint(x: to, y: at))
        }
        path.lineWidth = 3
        path.lineCapStyle = .round
        NSColor.controlAccentColor.setStroke()
        path.stroke()
    }

    /// Where a click would cut the hovered zone: down it, or across it with ⇧.
    private func drawGuide(_ raw: CGRect, zone: CGRect) {
        let at = guide(in: raw, at: mouse, across: shift)
        let path = NSBezierPath()
        if shift {
            path.move(to: CGPoint(x: zone.minX + 6, y: at))
            path.line(to: CGPoint(x: zone.maxX - 6, y: at))
        } else {
            path.move(to: CGPoint(x: at, y: zone.minY + 6))
            path.line(to: CGPoint(x: at, y: zone.maxY - 6))
        }
        path.lineWidth = 1.5
        path.setLineDash([5, 3], count: 2, phase: 0)
        NSColor.controlAccentColor.setStroke()
        path.stroke()
    }

    // MARK: - Pointer

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(rect: .zero, options: [.mouseMoved, .mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect],
                                  owner: self, userInfo: nil)
        addTrackingArea(area)
        tracking = area
    }

    /// The resize cursors over the lines; the whole view while one is dragged,
    /// so the cursor stays put when the pointer runs ahead of the line.
    override func resetCursorRects() {
        guard ready, let layout else { return }
        if let drag {
            addCursorRect(bounds, cursor: drag.vertical ? .resizeLeftRight : .resizeUpDown)
            return
        }
        for d in layout.dividers(in: canvas()) {
            addCursorRect(ZoneEditorView.grabArea(d), cursor: d.vertical ? .resizeLeftRight : .resizeUpDown)
        }
    }

    /// While the pointer is in the window, ⇧ turns the guide without a mouse
    /// move. A monitor rather than flagsChanged, which only reaches the first
    /// responder, and nothing here takes the keyboard.
    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if let flagsMonitor {
            NSEvent.removeMonitor(flagsMonitor)
            self.flagsMonitor = nil
        }
        guard window != nil else {
            // The timer holds on to the view; out of the window, let go.
            stopMorph()
            return
        }
        flagsMonitor = NSEvent.addLocalMonitorForEvents(matching: .flagsChanged) { [weak self] event in
            self?.shiftChanged(event.modifierFlags.contains(.shift))
            return event
        }
    }

    private func shiftChanged(_ on: Bool) {
        guard on != shift else { return }
        shift = on
        if hoverZone >= 0 { needsDisplay = true }
    }

    private func point(_ event: NSEvent) -> CGPoint {
        convert(event.locationInWindow, from: nil)
    }

    override func mouseEntered(with event: NSEvent) {
        shift = event.modifierFlags.contains(.shift)
        guard drag == nil else { return }
        hover(point(event))
        needsDisplay = true
    }

    override func mouseExited(with event: NSEvent) {
        guard drag == nil else { return }
        hoverZone = -1
        hoverDivider = nil
        needsDisplay = true
    }

    override func mouseMoved(with event: NSEvent) {
        shift = event.modifierFlags.contains(.shift)
        guard drag == nil else { return }
        hover(point(event))
        needsDisplay = true
    }

    override func mouseDown(with event: NSEvent) {
        // ⌃-click is a right-click on a Mac.
        if event.modifierFlags.contains(.control) {
            if let menu = menu(for: event) { NSMenu.popUpContextMenu(menu, with: event, for: self) }
            return
        }
        guard ready, let layout else { return }

        let p = point(event)
        let canvas = canvas()
        if let d = divider(at: p, in: canvas) {
            // A drag is followed point for point: no morph under it.
            stopMorph()
            drag = d
            hoverDivider = d
            hoverZone = -1
            needsDisplay = true
            window?.invalidateCursorRects(for: self)
            return
        }

        down = true
        downAt = p
        downZone = ZoneEditorView.zone(at: p, in: layout.zonesIn(canvas, spacing: 0))
    }

    override func mouseDragged(with event: NSEvent) {
        guard ready, let layout else { return }
        let p = point(event)
        shift = event.modifierFlags.contains(.shift)

        guard var d = drag else {
            hover(p)
            needsDisplay = true
            return
        }

        mouse = p
        let canvas = canvas()
        let start = d.vertical ? canvas.minX : canvas.minY
        let extent = d.vertical ? canvas.width : canvas.height
        var pos = d.vertical ? p.x : p.y

        if !event.modifierFlags.contains(.option) {
            for f in ZoneEditorView.lineStops {
                let c = start + (extent * f).rounded()
                if abs(pos - c) <= ZoneEditorView.snapReach {
                    pos = c
                    break
                }
            }
        }

        layout.moveDivider(&d, to: pos, minLength: (extent * ZoneEditorView.minShare).rounded())
        drag = d
        hoverDivider = d
        // Cursor rects can lag a fast drag; the cursor must not.
        (d.vertical ? NSCursor.resizeLeftRight : NSCursor.resizeUpDown).set()
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        guard ready, let layout else { return }
        let p = point(event)

        if drag != nil {
            drag = nil
            hover(p)
            edited()
            return
        }

        guard down else { return }
        down = false

        // A click, not a drag that wandered: barely moved, and let go in the
        // zone it was pressed in.
        if abs(p.x - downAt.x) >= ZoneEditorView.clickSlop || abs(p.y - downAt.y) >= ZoneEditorView.clickSlop { return }

        let canvas = canvas()
        let raw = layout.zonesIn(canvas, spacing: 0)
        let zone = ZoneEditorView.zone(at: p, in: raw)
        guard zone >= 0, zone == downZone else { return }

        let across = event.modifierFlags.contains(.shift)
        let z = raw[zone]
        let start = across ? z.minY : z.minX
        let extent = across ? z.height : z.width
        let cut = guide(in: z, at: p, across: across)

        // Both parts have to be worth having: no slivers.
        let least = (across ? canvas.height : canvas.width) * ZoneEditorView.minShare
        guard extent > 0, cut - start >= least, start + extent - cut >= least else { return }

        let before = shown()
        if layout.split(zone: zone, columns: !across, at: Double((cut - start) / extent)) {
            hover(p)
            startMorph(from: before)
            edited()
        }
    }

    // MARK: - Menu

    override func menu(for event: NSEvent) -> NSMenu? {
        guard ready, let layout else { return nil }
        let canvas = canvas()
        let raw = layout.zonesIn(canvas, spacing: 0)
        let zone = ZoneEditorView.zone(at: point(event), in: raw)
        guard zone >= 0 else { return nil }

        // The same floor as a click: halves under 5% of the display are slivers.
        menuZone = zone
        let menu = NSMenu()
        menu.autoenablesItems = false

        let columns = NSMenuItem(title: "Split Left and Right", action: #selector(splitColumns), keyEquivalent: "")
        columns.target = self
        columns.isEnabled = raw[zone].width / 2 >= canvas.width * ZoneEditorView.minShare
        menu.addItem(columns)

        let rows = NSMenuItem(title: "Split Top and Bottom", action: #selector(splitRows), keyEquivalent: "")
        rows.target = self
        rows.isEnabled = raw[zone].height / 2 >= canvas.height * ZoneEditorView.minShare
        menu.addItem(rows)

        menu.addItem(.separator())

        let remove = NSMenuItem(title: "Remove Zone", action: #selector(removeZone), keyEquivalent: "")
        remove.target = self
        remove.isEnabled = layout.zoneCount > 1
        menu.addItem(remove)
        return menu
    }

    @objc private func splitColumns() {
        let zone = menuZone
        edit { $0.split(zone: zone, columns: true, at: 0.5) }
    }

    @objc private func splitRows() {
        let zone = menuZone
        edit { $0.split(zone: zone, columns: false, at: 0.5) }
    }

    @objc private func removeZone() {
        let zone = menuZone
        edit { $0.remove(zone: zone) }
    }
}

/// The zone editor in the SwiftUI page.
struct ZoneEditor: NSViewRepresentable {
    let layout: ZoneLayout
    let displaySize: CGSize
    let spacing: CGFloat
    /// The model's revision: an edit or a reload redraws even when the
    /// layout handed over is the same object.
    let revision: Int
    let onCommit: () -> Void

    func makeNSView(context: Context) -> ZoneEditorView {
        ZoneEditorView()
    }

    func updateNSView(_ view: ZoneEditorView, context: Context) {
        view.onCommit = onCommit
        view.setLayout(layout, displaySize: displaySize, spacing: spacing)
    }
}
