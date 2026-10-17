import AppKit
import ZonesCore

/// Starts Zones: one copy only, the menu bar item, the engine, and on the
/// very first run the settings window, so a new user sees what it does.
final class AppDelegate: NSObject, NSApplicationDelegate {
    /// Sent by a second copy of Zones as it quits, so opening the app again
    /// (from Finder, Spotlight, at login twice) brings up the settings of
    /// the one already running instead of a second menu bar item.
    private static let showSettingsNotification = Notification.Name("com.ruibranco.zones.showSettings")

    /// Opened with --background (after an automatic update): nothing shows
    /// at launch, not even on a first run.
    private let background = CommandLine.arguments.contains(Updater.backgroundArgument)

    func applicationDidFinishLaunching(_ notification: Notification) {
        if let other = otherInstance() {
            if !background {
                // Delivered at once: macOS holds distributed notifications back
                // from an app that is not active, which the other copy is not.
                DistributedNotificationCenter.default().postNotificationName(
                    AppDelegate.showSettingsNotification, object: nil, userInfo: nil, deliverImmediately: true)
                other.activate(options: [])
            }
            NSApp.terminate(nil)
            return
        }

        DistributedNotificationCenter.default().addObserver(
            self, selector: #selector(showSettings(_:)), name: AppDelegate.showSettingsNotification,
            object: nil, suspensionBehavior: .deliverImmediately)

        // Asked before anything can save it.
        let firstRun = !FileManager.default.fileExists(atPath: Config.defaultURL.path)

        NSApp.mainMenu = AppDelegate.makeMainMenu()
        StatusItem.install()
        LoginItem.start()
        Engine.start()
        Updater.shared.start()

        if firstRun {
            AppState.shared.save()
            if !background { SettingsWindow.show() }
        }
    }

    @objc private func showSettings(_ notification: Notification) {
        SettingsWindow.show()
    }

    /// A menu bar app shows no menus, but its key equivalents still come
    /// from the main menu: without one, ⌘W and ⌘M do nothing in the
    /// settings window and ⌘Q does not quit, and Copy and Paste have nowhere
    /// to go in a text field. So a small standard one, never seen.
    private static func makeMainMenu() -> NSMenu {
        let main = NSMenu()

        let app = NSMenu()
        app.addItem(withTitle: "Quit Zones", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        main.addItem(withTitle: "Zones", action: nil, keyEquivalent: "").submenu = app

        let edit = NSMenu(title: "Edit")
        edit.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        main.addItem(withTitle: "Edit", action: nil, keyEquivalent: "").submenu = edit

        let window = NSMenu(title: "Window")
        window.addItem(withTitle: "Close", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        window.addItem(withTitle: "Minimize", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        main.addItem(withTitle: "Window", action: nil, keyEquivalent: "").submenu = window

        return main
    }

    /// Another Zones running already: by bundle id when this is the app, or
    /// for a bare build by the executable it runs.
    private func otherInstance() -> NSRunningApplication? {
        let me = ProcessInfo.processInfo.processIdentifier

        if let id = Bundle.main.bundleIdentifier {
            return NSRunningApplication.runningApplications(withBundleIdentifier: id)
                .first { $0.processIdentifier != me }
        }

        guard let path = Bundle.main.executableURL?.resolvingSymlinksInPath() else { return nil }
        return NSWorkspace.shared.runningApplications.first {
            $0.processIdentifier != me && $0.executableURL?.resolvingSymlinksInPath() == path
        }
    }
}
