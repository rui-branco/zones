import AppKit

/// Zones' one place in the menu bar: it has no Dock icon and no main
/// window, so this menu is how the settings are found and how it is quit.
enum StatusItem {
    private static var item: NSStatusItem?
    private static var actions: MenuActions?

    static func install() {
        guard item == nil else { return }

        let actions = MenuActions()
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item.button?.image = icon()
        item.button?.toolTip = "Zones"
        item.menu = menu(actions)

        self.item = item
        self.actions = actions
    }

    private static func menu(_ actions: MenuActions) -> NSMenu {
        let menu = NSMenu()
        menu.delegate = actions

        let settings = NSMenuItem(title: "Snap Layouts…", action: #selector(MenuActions.showSettings), keyEquivalent: ",")
        settings.target = actions
        menu.addItem(settings)

        let login = NSMenuItem(title: "Start at Login", action: #selector(MenuActions.toggleStartAtLogin), keyEquivalent: "")
        login.target = actions
        menu.addItem(login)
        actions.loginItem = login

        menu.addItem(.separator())

        // No action, so the menu greys it out: a label, not a command.
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev"
        menu.addItem(NSMenuItem(title: "Zones \(version)", action: nil, keyEquivalent: ""))

        let quit = NSMenuItem(title: "Quit Zones", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        quit.target = NSApp
        menu.addItem(quit)
        return menu
    }

    /// The mark, as a menu bar glyph: a screen with three zones, the middle
    /// one wider and lit, as a window lands in it. A template, so macOS
    /// tints it for a light or dark menu bar and for the pressed state.
    private static func icon() -> NSImage {
        let image = NSImage(size: NSSize(width: 18, height: 18), flipped: false) { _ in
            NSColor.black.set()

            let screen = NSBezierPath(roundedRect: NSRect(x: 1, y: 2.75, width: 16, height: 12.5),
                                      xRadius: 2.75, yRadius: 2.75)
            screen.lineWidth = 1.5
            screen.stroke()

            // Inside, 1pt clear of the outline: two outlined side zones and
            // a wider filled one between them, 1pt apart.
            for x in [3.25, 12.75] {
                let side = NSBezierPath(roundedRect: NSRect(x: x, y: 5, width: 2, height: 8), xRadius: 0.75, yRadius: 0.75)
                side.lineWidth = 1
                side.stroke()
            }
            NSBezierPath(roundedRect: NSRect(x: 6.75, y: 4.5, width: 4.5, height: 9), xRadius: 1.25, yRadius: 1.25).fill()
            return true
        }
        image.isTemplate = true
        image.accessibilityDescription = "Zones"
        return image
    }
}

/// The menu's target, and its delegate for ticking Start at Login as
/// macOS has it each time the menu opens.
private final class MenuActions: NSObject, NSMenuDelegate {
    weak var loginItem: NSMenuItem?

    @objc func showSettings() {
        SettingsWindow.show()
    }

    @objc func toggleStartAtLogin() {
        LoginItem.setEnabled(!LoginItem.isEnabled)
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        loginItem?.state = LoginItem.isEnabled ? .on : .off
    }
}
