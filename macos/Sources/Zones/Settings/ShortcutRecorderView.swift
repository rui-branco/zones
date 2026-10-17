import AppKit
import SwiftUI
import ZonesCore

/// A keyboard shortcut, shown as the keys to press and set by pressing them:
/// click it and it records the next combination. A text field would take
/// ⌃⌥Z for typing.
///
/// While it records, every key stops here before the window sees it, so ⌘W
/// cannot close the window under it. Esc on its own cancels, Delete clears,
/// and a shortcut needs ⌘, ⌃ or ⌥: ⇧ and a letter is only a capital. Nor can
/// it be one macOS or every app already answers to, ⌘Q and the like. The
/// modifiers show as they go down. Zones' own shortcuts are let go meanwhile,
/// so pressing the current one records it instead of moving a window.
final class ShortcutRecorderView: NSView {
    static let height: CGFloat = 24

    // Square at the least, and tight: ⌃⌥⇧⌘ Page Down still has to leave
    // its row's title room in a 272-point column.
    private static let capHeight: CGFloat = 20
    private static let innerCapHeight: CGFloat = 18     // a cap inside the recording box
    private static let capGap: CGFloat = 3
    private static let capPadding: CGFloat = 6
    private static let capFont = NSFont.systemFont(ofSize: 11, weight: .semibold)
    private static let promptFont = NSFont.systemFont(ofSize: 11)
    private static let notSet = "Not set"
    private static let prompt = "Type shortcut"

    // Carbon's modifier masks, which Shortcut and RegisterEventHotKey use.
    private static let command: UInt32 = 0x100
    private static let shift: UInt32 = 0x200
    private static let option: UInt32 = 0x800
    private static let control: UInt32 = 0x1000

    private static let escape: UInt32 = 0x35
    private static let delete: UInt32 = 0x33
    private static let forwardDelete: UInt32 = 0x75

    var shortcut: Shortcut? {
        didSet {
            if shortcut != oldValue { needsDisplay = true }
        }
    }

    var isEnabled = true {
        didSet {
            guard isEnabled != oldValue else { return }
            if !isEnabled { cancel() }
            needsDisplay = true
        }
    }

    /// A new shortcut was recorded, or the old one cleared (nil).
    var onChange: ((Shortcut?) -> Void)?

    /// Recording started or ended, or the modifiers held changed: the width
    /// to give the recorder changes with each.
    var onRecordingChange: ((Bool, UInt32) -> Void)?

    private(set) var isRecording = false
    private var held: UInt32 = 0
    private var hovering = false
    private var monitor: Any?
    private var tracking: NSTrackingArea?

    override var isFlipped: Bool {
        true
    }

    // MARK: - Size

    /// As wide as the keys as drawn or, while recording, the keys held and
    /// the prompt after them.
    static func width(for shortcut: Shortcut?, recording: Bool, held: UInt32) -> CGFloat {
        if recording {
            let mods = modifierNames(held)
            var w: CGFloat = 6 + mods.reduce(0) { $0 + capWidth($1, innerCapHeight) + capGap }
            if !mods.isEmpty { w += 2 }
            return ceil(w + textWidth(prompt, promptFont) + 8)
        }
        let keys = shortcut?.parts ?? [notSet]
        return keys.reduce(0) { $0 + capWidth($1, capHeight) } + capGap * CGFloat(keys.count - 1)
    }

    private static func capWidth(_ name: String, _ height: CGFloat) -> CGFloat {
        max(height, ceil(textWidth(name, capFont)) + 2 * capPadding)
    }

    private static func textWidth(_ text: String, _ font: NSFont) -> CGFloat {
        NSAttributedString(string: text, attributes: [.font: font]).size().width
    }

    /// The symbols of the modifiers in `mods`, in the order Shortcut draws them.
    private static func modifierNames(_ mods: UInt32) -> [String] {
        Array(Shortcut(keyCode: 0, modifiers: mods).parts.dropLast())
    }

    /// AppKit's modifier flags as Carbon's mask, the four a shortcut can carry.
    private static func carbon(_ flags: NSEvent.ModifierFlags) -> UInt32 {
        var mods: UInt32 = 0
        if flags.contains(.command) { mods |= command }
        if flags.contains(.shift) { mods |= shift }
        if flags.contains(.option) { mods |= option }
        if flags.contains(.control) { mods |= control }
        return mods
    }

    /// The combinations macOS itself, or every app, already answers to:
    /// quitting, closing a window, switching apps, Spotlight, locking the
    /// screen, Force Quit. Some never reach the recorder at all; the rest
    /// would reach Zones and stop doing what everyone expects of them.
    private static func reserved(_ mods: UInt32, _ key: UInt32) -> Bool {
        switch mods {
        case command: return key == 0x0C || key == 0x0D || key == 0x30 || key == 0x31   // Q W Tab Space
        case control | command: return key == 0x0C                                       // Q
        case command | option: return key == escape
        default: return false
        }
    }

    // MARK: - Painting

    override func draw(_ dirtyRect: NSRect) {
        if isRecording {
            drawRecording()
            return
        }

        let keys = shortcut?.parts ?? [ShortcutRecorderView.notSet]
        let ink: NSColor = !isEnabled ? .tertiaryLabelColor : (shortcut == nil ? .secondaryLabelColor : .labelColor)
        let h = ShortcutRecorderView.capHeight
        let y = ((bounds.height - h) / 2).rounded()
        var x: CGFloat = 0
        for key in keys {
            // Held narrower than its keys (see SnappingCard), the last one is
            // cut short: views do not clip their drawing, and anything past
            // the edge would run over the card's margin.
            let room = bounds.width - x
            guard room >= h else { break }
            let cap = CGRect(x: x, y: y, width: min(ShortcutRecorderView.capWidth(key, h), room), height: h)
            drawCap(key, in: cap, ink: ink, hover: hovering && isEnabled)
            x = cap.maxX + ShortcutRecorderView.capGap
        }
    }

    /// Recording: a box edged in the accent, as a text field with the caret
    /// in it is, holding the modifiers down so far as keys, then the prompt.
    private func drawRecording() {
        let box = NSBezierPath(roundedRect: bounds.insetBy(dx: 0.75, dy: 0.75), xRadius: 5, yRadius: 5)
        NSColor.controlBackgroundColor.setFill()
        box.fill()
        NSColor.controlAccentColor.setStroke()
        box.lineWidth = 1.5
        box.stroke()

        let mods = ShortcutRecorderView.modifierNames(held)
        let h = ShortcutRecorderView.innerCapHeight
        let y = ((bounds.height - h) / 2).rounded()
        var x: CGFloat = 6
        for key in mods {
            let cap = CGRect(x: x, y: y, width: ShortcutRecorderView.capWidth(key, h), height: h)
            drawCap(key, in: cap, ink: .labelColor, hover: false)
            x = cap.maxX + ShortcutRecorderView.capGap
        }
        if !mods.isEmpty { x += 2 }

        // Cut short when the recorder is held narrower than the keys and the
        // words, so the row's title keeps its room.
        let style = NSMutableParagraphStyle()
        style.lineBreakMode = .byTruncatingTail
        let text = NSAttributedString(string: ShortcutRecorderView.prompt, attributes: [
            .font: ShortcutRecorderView.promptFont,
            .foregroundColor: NSColor.secondaryLabelColor,
            .paragraphStyle: style,
        ])
        let textHeight = text.size().height
        text.draw(with: CGRect(x: x, y: (bounds.height - textHeight) / 2, width: max(0, bounds.width - 8 - x), height: textHeight),
                  options: [.usesLineFragmentOrigin, .truncatesLastVisibleLine])
    }

    /// One key as a keycap: a rounded plate with a hairline edge, the name in the middle.
    private func drawCap(_ name: String, in rect: CGRect, ink: NSColor, hover: Bool) {
        let plate = NSBezierPath(roundedRect: rect.insetBy(dx: 0.5, dy: 0.5), xRadius: 4, yRadius: 4)
        NSColor.controlBackgroundColor.setFill()
        plate.fill()
        if hover {
            Palette.hover.setFill()
            plate.fill()
        }
        NSColor.separatorColor.setStroke()
        plate.lineWidth = 1
        plate.stroke()

        // Centred; in a cap cut short, cut short too.
        let style = NSMutableParagraphStyle()
        style.alignment = .center
        style.lineBreakMode = .byTruncatingTail
        let text = NSAttributedString(string: name, attributes: [
            .font: ShortcutRecorderView.capFont,
            .foregroundColor: ink,
            .paragraphStyle: style,
        ])
        let textHeight = text.size().height
        text.draw(with: CGRect(x: rect.minX + 3, y: rect.midY - textHeight / 2, width: max(0, rect.width - 6), height: textHeight),
                  options: [.usesLineFragmentOrigin, .truncatesLastVisibleLine])
    }

    // MARK: - Pointer

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(rect: .zero, options: [.mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect],
                                  owner: self, userInfo: nil)
        addTrackingArea(area)
        tracking = area
    }

    override func mouseEntered(with event: NSEvent) {
        hovering = true
        needsDisplay = true
    }

    override func mouseExited(with event: NSEvent) {
        hovering = false
        needsDisplay = true
    }

    override func mouseDown(with event: NSEvent) {
        guard isEnabled, !isRecording else { return }
        start()
    }

    // MARK: - Recording

    private func start() {
        guard let window else { return }
        isRecording = true
        held = ShortcutRecorderView.carbon(NSEvent.modifierFlags)
        Hotkeys.shared.suspend()

        monitor = NSEvent.addLocalMonitorForEvents(
            matching: [.keyDown, .flagsChanged, .leftMouseDown, .rightMouseDown, .otherMouseDown]
        ) { [weak self] event in
            guard let self else { return event }
            return self.handle(event)
        }

        // Another window came up, or this one went: no new shortcut, so the
        // old one stays.
        NotificationCenter.default.addObserver(
            self, selector: #selector(windowLetGo(_:)), name: NSWindow.didResignKeyNotification, object: window)
        NotificationCenter.default.addObserver(
            self, selector: #selector(windowLetGo(_:)), name: NSWindow.willCloseNotification, object: window)

        needsDisplay = true
        onRecordingChange?(true, held)
    }

    /// Every key while recording; nil swallows it.
    private func handle(_ event: NSEvent) -> NSEvent? {
        switch event.type {
        case .keyDown:
            press(event)
            return nil
        case .flagsChanged:
            heldChanged(ShortcutRecorderView.carbon(event.modifierFlags))
            return event
        default:
            // Clicked away: cancelled, and the click still does what it does.
            if event.window !== window || !bounds.contains(convert(event.locationInWindow, from: nil)) {
                cancel()
            }
            return event
        }
    }

    private func heldChanged(_ mods: UInt32) {
        guard mods != held else { return }
        held = mods
        needsDisplay = true
        onRecordingChange?(true, held)
    }

    private func press(_ event: NSEvent) {
        let mods = ShortcutRecorderView.carbon(event.modifierFlags)
        let key = UInt32(event.keyCode)
        heldChanged(mods)

        if mods == 0 && key == ShortcutRecorderView.escape {
            cancel()
            return
        }
        if mods == 0 && (key == ShortcutRecorderView.delete || key == ShortcutRecorderView.forwardDelete) {
            finish(with: nil)
            return
        }

        // ⇧ is not enough on its own: ⇧ and a letter is typing, and a
        // shortcut that is typing would fire in every text field.
        let needed = ShortcutRecorderView.command | ShortcutRecorderView.option | ShortcutRecorderView.control
        if mods & needed == 0 { return }

        // Nor one that is already spoken for: it is ignored the same way, and
        // the recorder goes on waiting for a shortcut it can have.
        if ShortcutRecorderView.reserved(mods, key) { return }

        finish(with: Shortcut(keyCode: key, modifiers: mods))
    }

    @objc private func windowLetGo(_ note: Notification) {
        cancel()
    }

    override func viewWillMove(toWindow newWindow: NSWindow?) {
        super.viewWillMove(toWindow: newWindow)
        if newWindow == nil { cancel() }
    }

    private func end() {
        if let monitor {
            NSEvent.removeMonitor(monitor)
            self.monitor = nil
        }
        NotificationCenter.default.removeObserver(self, name: NSWindow.didResignKeyNotification, object: nil)
        NotificationCenter.default.removeObserver(self, name: NSWindow.willCloseNotification, object: nil)
        isRecording = false
        held = 0
        needsDisplay = true
        onRecordingChange?(false, 0)
    }

    /// Ends recording with the old shortcut kept, which takes its keys back.
    func cancel() {
        guard isRecording else { return }
        end()
        Hotkeys.shared.resume()
    }

    /// Ends recording with a new shortcut, or none. Whoever stores it
    /// registers the shortcuts again, the new one included.
    private func finish(with shortcut: Shortcut?) {
        end()
        onChange?(shortcut)
    }
}

/// The shortcut recorder in the SwiftUI page.
struct ShortcutField: NSViewRepresentable {
    let shortcut: Shortcut?
    let enabled: Bool
    let onChange: (Shortcut?) -> Void
    let onRecordingChange: (Bool, UInt32) -> Void

    func makeNSView(context: Context) -> ShortcutRecorderView {
        ShortcutRecorderView()
    }

    func updateNSView(_ view: ShortcutRecorderView, context: Context) {
        view.onChange = onChange
        view.onRecordingChange = onRecordingChange
        view.shortcut = shortcut
        view.isEnabled = enabled
    }
}
