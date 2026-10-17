import AppKit
import ZonesCore

/// What the settings window shows, and everything it does to the config,
/// kept out of the views so they only draw it.
///
/// The config is the one truth: every control reads it, and every change
/// writes it and saves it there and then, so there is no OK button to forget.
/// The exception is the layout in the zone editor, `editing`: a copy the
/// editor changes in place while a line is dragged, which reaches the config
/// only once an edit is finished, in `commitEditing`, so a drag saves once
/// rather than on every mouse move.
@MainActor
final class SettingsModel: NSObject, ObservableObject {
    /// How many sizes a display can have: plenty to flip between, few enough
    /// for the strip to stay one short row beside the "Zones" heading.
    static let maxSizes = 6

    @Published private(set) var displays: [Display] = []
    @Published private(set) var selected: Display?

    /// The selected display's active size, as the zone editor edits it.
    @Published private(set) var editing = ZoneLayout.preset(0)

    /// Bumped on every change to the config and to `editing` in place:
    /// neither is published by itself, and the views read both.
    @Published private(set) var revision = 0

    /// The shortcut slot being recorded, if any, and the modifiers held for
    /// it so far: the recorder grows with them, so its row lays out again.
    @Published private(set) var recording: Hotkeys.Slot?
    @Published private(set) var heldModifiers: UInt32 = 0

    /// "Use on All Displays" just worked: the Presets heading's line says so
    /// for a moment, as nothing else on screen changes to show it.
    @Published private(set) var copied = false

    /// What the updater is doing: the General page's update button shows it,
    /// and the sidebar's foot an update on offer.
    @Published private(set) var update = Updater.shared.state

    /// Set while this window's own change is being saved, so the
    /// config-changed notification that save posts is not taken for someone
    /// else's change and reloaded.
    private var saving = false

    /// - Parameter observing: follow config and display changes. Off for a
    ///   snapshot, which is drawn once and thrown away.
    init(observing: Bool) {
        super.init()
        displays = Displays.all()
        selected = startDisplay()
        load()

        guard observing else { return }
        // A shortcut can flip a display's size while the window is open, and
        // a display can come or go: both have to show here.
        NotificationCenter.default.addObserver(
            self, selector: #selector(configChanged(_:)), name: .zonesConfigChanged, object: nil)
        NotificationCenter.default.addObserver(
            self, selector: #selector(screensChanged(_:)), name: NSApplication.didChangeScreenParametersNotification,
            object: nil)
        NotificationCenter.default.addObserver(
            self, selector: #selector(updateChanged(_:)), name: .zonesUpdateChanged, object: nil)
    }

    /// The window closed: nothing more to follow.
    func stop() {
        NotificationCenter.default.removeObserver(self)
        NSObject.cancelPreviousPerformRequests(withTarget: self)
    }

    var config: Config {
        AppState.shared.config
    }

    // MARK: - Displays

    /// The display under the mouse, where the user is looking; else the main one.
    private func startDisplay() -> Display? {
        let mouse = Displays.toYDown(CGRect(origin: NSEvent.mouseLocation, size: .zero)).origin
        let here = Displays.at(mouse)
        return displays.first { $0.key == here?.key }
            ?? displays.first { $0.isMain }
            ?? displays.first
    }

    func select(_ display: Display) {
        guard display.key != selected?.key else { return }
        selected = display
        load()
    }

    /// A display's sizes: the ones stored for it, or its default. The default
    /// is worked out from the visible frame, as the engine does, so the
    /// window shows the very layout a drag snaps to.
    func layouts(of display: Display) -> DisplayLayouts {
        config.layouts(for: display.key, size: display.visibleFrame.size)
    }

    /// "1800 × 1169  ·  Main": what tells two displays apart.
    static func detail(_ display: Display) -> String {
        "\(Int(display.frame.width)) × \(Int(display.frame.height))" + (display.isMain ? "  ·  Main" : "")
    }

    /// Puts the selected display's active size in the editor. A copy: the
    /// editor changes it in place, and nothing should reach the config but a
    /// `commitEditing`.
    private func load() {
        if let display = selected {
            editing = layouts(of: display).current.clone()
        }
        revision += 1
    }

    @objc private func configChanged(_ note: Notification) {
        if !saving { load() }
    }

    /// Lists the displays again, staying on the same one if it is still there.
    @objc private func screensChanged(_ note: Notification) {
        let keep = selected?.key
        displays = Displays.all()
        selected = displays.first { $0.key == keep } ?? startDisplay()
        load()
    }

    private func save() {
        saving = true
        AppState.shared.save()
        saving = false
    }

    // MARK: - Options

    func setSnapEnabled(_ on: Bool) {
        config.snapEnabled = on
        optionsChanged()
    }

    func setHoldShift(_ on: Bool) {
        config.holdShift = on
        optionsChanged()
    }

    func setSpacing(_ points: Int) {
        config.spacing = points
        optionsChanged()
    }

    /// The shortcut rows, in the order the Shortcuts card lists them.
    static let shortcutSlots: [Hotkeys.Slot] = [.zone, .zoneBack, .size, .sizeBack]

    /// A shortcut's name: its row's title, and how the line under the card
    /// refers to it when another app has it.
    static func title(_ slot: Hotkeys.Slot) -> String {
        switch slot {
        case .zone: return "Next zone"
        case .zoneBack: return "Previous zone"
        case .size: return "Next size"
        case .sizeBack: return "Previous size"
        }
    }

    func shortcut(_ slot: Hotkeys.Slot) -> Shortcut? {
        switch slot {
        case .zone: return config.zoneShortcut
        case .zoneBack: return config.zoneBackShortcut
        case .size: return config.sizeShortcut
        case .sizeBack: return config.sizeBackShortcut
        }
    }

    func setShortcut(_ shortcut: Shortcut?, for slot: Hotkeys.Slot) {
        switch slot {
        case .zone: config.zoneShortcut = shortcut
        case .zoneBack: config.zoneBackShortcut = shortcut
        case .size: config.sizeShortcut = shortcut
        case .sizeBack: config.sizeBackShortcut = shortcut
        }
        optionsChanged()
    }

    /// The shortcuts another app already has, so they do nothing until
    /// changed. Not the one being recorded: its old keys are let go anyway.
    var takenSlots: [Hotkeys.Slot] {
        SettingsModel.shortcutSlots.filter { Hotkeys.shared.failed.contains($0) && $0 != recording }
    }

    /// Saved, and the shortcuts registered again: they depend on snapping
    /// being on as well as on the keys, and a new one may be taken or free.
    private func optionsChanged() {
        save()
        Hotkeys.shared.apply(config)
        revision += 1
    }

    func recordingChanged(_ slot: Hotkeys.Slot, recording on: Bool, held: UInt32) {
        if on {
            recording = slot
            heldModifiers = held
        } else if recording == slot {
            recording = nil
            heldModifiers = 0
        }
    }

    // MARK: - Layouts

    /// Saves the layout being edited as the selected display's active size,
    /// keeping its other sizes.
    func commitEditing() {
        guard let display = selected else { return }
        var layouts = layouts(of: display)
        layouts.sizes[layouts.active] = editing.clone()
        config.layouts[display.key] = layouts
        save()
        revision += 1
    }

    /// Replaces the active size's layout with ready-made layout `index`.
    func pickPreset(_ index: Int) {
        editing = ZoneLayout.preset(index)
        commitEditing()
    }

    /// The engine flips the size, moving the windows snapped to the old one
    /// and saving; the window then shows whatever it left in the config.
    func activateSize(_ index: Int) {
        guard let display = selected else { return }
        saving = true
        Engine.activateSize(index, on: display)
        saving = false
        load()
    }

    /// A new size, starting as a copy of the active one: a new size is
    /// nearly always the current one a bit wider or narrower.
    func addSize() {
        guard let display = selected else { return }
        var layouts = layouts(of: display)
        guard layouts.sizes.count < SettingsModel.maxSizes else { return }
        layouts.sizes.append(layouts.current.clone())
        config.layouts[display.key] = layouts
        save()
        activateSize(layouts.sizes.count - 1)
    }

    /// Takes size `index` away. The active size stays the same layout, so
    /// nothing moves; if it was the one taken, the one before it takes over.
    func removeSize(_ index: Int) {
        guard let display = selected else { return }
        var layouts = layouts(of: display)
        guard layouts.sizes.count > 1, layouts.sizes.indices.contains(index) else { return }

        let active = layouts.active
        layouts.sizes.remove(at: index)
        let next = index < active ? active - 1 : (index == active ? max(index - 1, 0) : active)
        layouts.active = next
        config.layouts[display.key] = layouts
        save()
        activateSize(next)
    }

    /// Shows the layout on the display itself for a moment, which is what
    /// tells two displays of the same make apart.
    func showOnScreen() {
        guard let display = selected else { return }
        let zones = editing.zonesIn(display.visibleFrame, spacing: CGFloat(config.spacing))
        Overlay.shared.flash(display: display, zones: zones, label: nil, duration: 1.5)
    }

    /// Gives every display the selected one's sizes, each its own copy so
    /// editing one later leaves the others be.
    func useOnAllDisplays() {
        guard let display = selected else { return }
        let source = layouts(of: display)
        for other in displays {
            config.layouts[other.key] = DisplayLayouts(sizes: source.sizes.map { $0.clone() }, active: source.active)
        }
        save()
        revision += 1

        copied = true
        NSObject.cancelPreviousPerformRequests(withTarget: self, selector: #selector(copiedDone), object: nil)
        perform(#selector(copiedDone), with: nil, afterDelay: 2)
    }

    @objc private func copiedDone() {
        copied = false
    }

    // MARK: - Updates

    @objc private func updateChanged(_ note: Notification) {
        update = Updater.shared.state
    }

    /// Checks now, saying what it found in an alert, as the menu's Check
    /// for Updates… does.
    func checkForUpdates() {
        Updater.shared.check(manual: true)
    }

    func installUpdate() {
        Updater.shared.install()
    }

    /// Turned on, an update already on offer installs by itself. The menu
    /// bar's tick reads the config each time its menu opens.
    func setAutoUpdate(_ on: Bool) {
        config.autoUpdate = on
        save()
        revision += 1
        Updater.shared.autoUpdateChanged()
    }

    // MARK: - Start at login

    /// What macOS has, as the menu bar's Start at Login shows it: the user
    /// can take Zones off the list in System Settings without asking us.
    var startsAtLogin: Bool {
        LoginItem.isEnabled
    }

    /// As the menu bar's Start at Login does: on the login list or off it,
    /// the config following and saved, the user told why when it cannot be
    /// put on. Drawn again either way, so a switch that did not take goes back.
    func setStartAtLogin(_ on: Bool) {
        LoginItem.setEnabled(on)
        revision += 1
    }

    // MARK: - Reset

    /// Back to the display's default, by forgetting what was chosen for it,
    /// once the user has said so: every size it has goes with it.
    func reset() {
        guard let display = selected else { return }
        let sizes = layouts(of: display).sizes.count
        let alert = NSAlert()
        alert.messageText = "Reset \(display.name)?"
        alert.informativeText = sizes > 1
            ? "Its layout and all \(sizes) sizes go back to the default. This can't be undone."
            : "Its layout goes back to the default. This can't be undone."
        alert.alertStyle = .warning
        alert.addButton(withTitle: "Reset").hasDestructiveAction = true
        alert.addButton(withTitle: "Cancel")
        guard alert.runModal() == .alertFirstButtonReturn else { return }

        config.layouts.removeValue(forKey: display.key)
        save()
        load()
    }
}
