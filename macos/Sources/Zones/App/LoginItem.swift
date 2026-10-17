import AppKit
import ServiceManagement

/// Launching at login, through SMAppService: macOS keeps the list, and the
/// user can take Zones off it in System Settings without asking us. So what
/// macOS says is what counts, and `config.startAtLogin` is kept matching it:
/// at launch, after every change here, and when the settings window turns
/// the setting on or off and saves.
enum LoginItem {
    private static var observer: NSObjectProtocol?

    /// On the list, or put on it and waiting for the user to approve it in
    /// System Settings.
    static var isEnabled: Bool {
        let status = SMAppService.mainApp.status
        return status == .enabled || status == .requiresApproval
    }

    /// At launch: the config takes macOS's word, then follows the setting
    /// as it changes.
    static func start() {
        sync()
        guard observer == nil else { return }
        observer = NotificationCenter.default.addObserver(
            forName: .zonesConfigChanged, object: nil, queue: .main
        ) { _ in
            let wanted = AppState.shared.config.startAtLogin
            if wanted != isEnabled { setEnabled(wanted) }
        }
    }

    /// Puts Zones on the login list or takes it off. Only an app bundle can
    /// be on it: run straight from the build folder, registering throws,
    /// and the user is told how to get the installed app instead.
    static func setEnabled(_ on: Bool) {
        do {
            if on {
                try SMAppService.mainApp.register()
            } else {
                try SMAppService.mainApp.unregister()
            }
        } catch {
            if on { showNeedsInstall() }
        }
        sync()
    }

    private static func sync() {
        let config = AppState.shared.config
        let enabled = isEnabled
        guard config.startAtLogin != enabled else { return }
        config.startAtLogin = enabled
        AppState.shared.save()
    }

    private static func showNeedsInstall() {
        let alert = NSAlert()
        alert.messageText = "Start at Login needs the installed app"
        alert.informativeText = "Install Zones with scripts/install.sh, then turn this on from the copy in Applications."
        alert.alertStyle = .informational
        if #available(macOS 14, *) {
            NSApp.activate()
        } else {
            NSApp.activate(ignoringOtherApps: true)
        }
        alert.runModal()
    }
}
