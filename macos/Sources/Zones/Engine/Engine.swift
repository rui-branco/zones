import AppKit
import ZonesCore

/// What the rest of the app asks of the snapping engine.
///
/// CONTRACT: the app delegate calls start; the settings window calls
/// activateSize when a size thumbnail is picked.
enum Engine {
    /// Starts everything once at launch: the Accessibility check, watching
    /// drags, the shortcuts.
    static func start() {
        Permissions.start()

        // The shortcuts need no permission to fire, but everything they do
        // moves other apps' windows, which does: without it, a press asks.
        Hotkeys.shared.onPress = { slot, step in
            guard Permissions.isTrusted else {
                Permissions.showOnboarding()
                return
            }
            switch slot {
            case .zone, .zoneBack: Cycler.cycle(step)
            case .size, .sizeBack: flipSize(step)
            }
        }
        Hotkeys.shared.apply(AppState.shared.config)

        // macOS hands out the event tap only once Zones is allowed: now, or
        // the moment the user turns it on.
        Permissions.whenTrusted { DragSnapper.shared.start() }
    }

    /// Makes `index` the active size of `display`, moves the windows snapped
    /// there into the matching zones of that size, saves, and flashes the
    /// zones with a "Size n of m" label.
    static func activateSize(_ index: Int, on display: Display) {
        activateSize(index, on: display, flash: true)
    }

    /// `activateSize`, with the flash optional: a drop that picked its size
    /// by scrolling has just had the zones on screen, and puts its window
    /// straight in.
    static func activateSize(_ index: Int, on display: Display, flash: Bool) {
        let config = AppState.shared.config
        let spacing = CGFloat(config.spacing)
        var layouts = config.layouts(for: display.key, size: display.visibleFrame.size)
        let oldIndex = layouts.active
        let oldZones = layouts.current.zonesIn(display.visibleFrame, spacing: spacing)

        layouts.active = min(max(index, 0), layouts.sizes.count - 1)
        config.layouts[display.key] = layouts
        AppState.shared.save()

        let zones = layouts.current.zonesIn(display.visibleFrame, spacing: spacing)
        if Permissions.isTrusted {
            // The same size again has no old zones to leave: only Zones' own
            // windows are put back, in case the size was edited.
            reflow(display, from: oldIndex == layouts.active ? nil : oldZones, to: zones, sizeIndex: layouts.active)
        }

        if flash {
            Overlay.shared.flash(display: display, zones: zones,
                                 label: "Size \(layouts.active + 1) of \(layouts.sizes.count)", duration: 0.7)
        }
    }

    /// The size shortcut: the display under the focused window, or under
    /// the mouse with none, flips to its next size, `step` +1 or -1.
    private static func flipSize(_ step: Int) {
        let config = AppState.shared.config
        guard config.snapEnabled, let display = shortcutDisplay() else { return }

        let layouts = config.layouts(for: display.key, size: display.visibleFrame.size)
        let n = layouts.sizes.count
        guard n >= 2 else {
            // Nothing to flip to: show what there is, and where to get more.
            let zones = layouts.current.zonesIn(display.visibleFrame, spacing: CGFloat(config.spacing))
            Overlay.shared.flash(display: display, zones: zones,
                                 label: "One size  ·  add more in Zones settings", duration: 1.2)
            return
        }

        activateSize(((layouts.active + step) % n + n) % n, on: display)
    }

    private static func shortcutDisplay() -> Display? {
        if let window = WindowRef.focused(), !window.isMinimized, let frame = window.frame {
            return Displays.at(CGPoint(x: frame.midX, y: frame.midY))
        }
        let mouse = NSEvent.mouseLocation
        return Displays.at(Displays.toYDown(CGRect(origin: mouse, size: .zero)).origin)
    }

    /// Takes the windows in the zones of `display` along to `zones`, the
    /// new size's. First the ones Zones put there itself, still where it
    /// left them: into the same zones of the new size, when it has them all.
    /// Then any other window that sits exactly in one of the old size's
    /// zones (put there by hand, or by another tool) goes to the zone with
    /// the same number.
    private static func reflow(_ display: Display, from oldZones: [CGRect]?, to zones: [CGRect], sizeIndex: Int) {
        var moved = Set<WindowRef>()

        for (window, placed) in Placements.all where placed.displayKey == display.key {
            guard let first = placed.indices.first,
                  placed.indices.allSatisfy({ $0 >= 0 && $0 < zones.count }),
                  let frame = window.frame, frame.matches(placed.frame, within: 2) else { continue }

            let target = placed.indices.dropFirst().reduce(zones[first]) { $0.union(zones[$1]) }
            Placements.place(window, target, displayKey: display.key, indices: placed.indices, sizeIndex: sizeIndex)
            moved.insert(window)
        }

        guard let oldZones = oldZones else { return }
        for window in WindowRef.windows(on: display) where !moved.contains(window) {
            guard let frame = window.frame,
                  let i = oldZones.firstIndex(where: { $0.matches(frame, within: 2) }), i < zones.count else { continue }
            Placements.place(window, zones[i], displayKey: display.key, indices: [i], sizeIndex: sizeIndex)
        }
    }
}
