import AppKit
import SwiftUI

/// The settings window.
///
/// CONTRACT: the menu bar item and the app delegate call show(); main.swift
/// calls renderSnapshot when launched with `--render-settings <path>`.
/// Main thread only, like the rest of the app.
enum SettingsWindow {
    /// Opens the window, or brings back the one already open, and makes
    /// Zones the active app so it comes to the front.
    static func show() {
        MainActor.assumeIsolated {
            SettingsWindowController.show()
        }
    }

    /// Draws the settings content, as it would look now, into a PNG at
    /// `path` without showing a window: for checking the design over ssh.
    /// `dark` picks the appearance. True on success.
    @discardableResult
    static func renderSnapshot(to path: String, dark: Bool) -> Bool {
        MainActor.assumeIsolated {
            SettingsSnapshot.render(to: path, dark: dark)
        }
    }
}

/// The one settings window there is, while it is open.
///
/// A titled window, so it has the traffic lights and a place in Mission
/// Control, but with the title bar see-through and its title hidden: the page
/// draws its own flat header under them, in the window's colour. Resizable,
/// for the zone editor's sake: a bigger model display is easier to draw on.
@MainActor
private final class SettingsWindowController: NSObject, NSWindowDelegate {
    private static var open: SettingsWindowController?

    private let window: NSWindow
    private let model: SettingsModel

    static func show() {
        if let open {
            open.bringForward()
            return
        }
        let controller = SettingsWindowController()
        open = controller
        controller.bringForward()
    }

    private override init() {
        // Opens where the user is looking: on the display under the mouse,
        // at the default size where that fits, never bigger than most of it.
        let mouse = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { NSMouseInRect(mouse, $0.frame, false) } ?? NSScreen.main
        var size = Metrics.size
        if let area = screen?.visibleFrame {
            size.width = max(Metrics.minSize.width, min(size.width, (area.width * 0.92).rounded(.down)))
            size.height = max(Metrics.minSize.height, min(size.height, (area.height * 0.92).rounded(.down)))
        }

        model = SettingsModel(observing: true)
        window = NSWindow(
            contentRect: NSRect(origin: .zero, size: size),
            styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
            backing: .buffered,
            defer: false)
        super.init()

        window.title = "Zones"      // what Mission Control and the Window menu call it
        window.titleVisibility = .hidden
        window.titlebarAppearsTransparent = true
        window.isReleasedWhenClosed = false

        // An empty toolbar, only for its title bar height: in the compact
        // style the traffic lights sit centred in the top 40 points, level
        // with the icon and the name drawn in the header. Nothing in it to
        // customise or show as text.
        let toolbar = NSToolbar(identifier: "ZonesSettings")
        toolbar.allowsUserCustomization = false
        if #available(macOS 15.0, *) {
            toolbar.allowsDisplayModeCustomization = false
        }
        window.toolbar = toolbar
        window.toolbarStyle = .unifiedCompact
        window.titlebarSeparatorStyle = .none

        let host = NSHostingView(rootView: SettingsView(model: model))
        // The window's size is the user's; the page fits itself to it and
        // does not get to change it.
        host.sizingOptions = []
        window.contentView = host
        window.delegate = self

        if let area = screen?.visibleFrame {
            let frame = window.frame.size
            window.setFrameOrigin(NSPoint(x: (area.midX - frame.width / 2).rounded(),
                                          y: (area.midY - frame.height / 2).rounded()))
        }
    }

    /// Zones has no Dock icon, so the window only comes in front of the app
    /// being used if Zones is made the active app first.
    private func bringForward() {
        if window.isMiniaturized { window.deminiaturize(nil) }
        NSApp.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
    }

    /// No smaller than the page works at. Here rather than contentMinSize:
    /// with a toolbar, AppKit takes the window's limits from its content's
    /// constraints and drops contentMinSize. The content fills the whole
    /// frame, title bar included, so the frame's size is the content's.
    func windowWillResize(_ sender: NSWindow, to frameSize: NSSize) -> NSSize {
        NSSize(width: max(frameSize.width, Metrics.minSize.width),
               height: max(frameSize.height, Metrics.minSize.height))
    }

    func windowWillClose(_ notification: Notification) {
        model.stop()
        // Out of the window, the page's views let go of their event
        // monitors, and a shortcut being recorded is cancelled and given back.
        window.contentView = nil
        window.delegate = nil
        SettingsWindowController.open = nil
    }
}

/// The page drawn into a PNG with no window: the same view, on the current
/// config and displays, at 2x.
@MainActor
private enum SettingsSnapshot {
    static func render(to path: String, dark: Bool) -> Bool {
        _ = NSApplication.shared

        let model = SettingsModel(observing: false)
        let host = NSHostingView(rootView: SettingsView(model: model))
        host.appearance = NSAppearance(named: dark ? .darkAqua : .aqua)
        host.sizingOptions = []
        host.frame = NSRect(origin: .zero, size: Metrics.size)
        host.layoutSubtreeIfNeeded()

        guard let rep = NSBitmapImageRep(
            bitmapDataPlanes: nil,
            pixelsWide: Int(Metrics.size.width * 2),
            pixelsHigh: Int(Metrics.size.height * 2),
            bitsPerSample: 8,
            samplesPerPixel: 4,
            hasAlpha: true,
            isPlanar: false,
            colorSpaceName: .deviceRGB,
            bytesPerRow: 0,
            bitsPerPixel: 0)
        else { return false }
        rep.size = Metrics.size

        // Draws the AppKit parts too, the zone editor and the shortcut
        // recorders, which SwiftUI's own ImageRenderer leaves out.
        host.cacheDisplay(in: host.bounds, to: rep)

        guard let png = rep.representation(using: .png, properties: [:]) else { return false }
        do {
            try png.write(to: URL(fileURLWithPath: path), options: .atomic)
            return true
        } catch {
            return false
        }
    }
}
