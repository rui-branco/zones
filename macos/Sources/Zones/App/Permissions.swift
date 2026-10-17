import AppKit
import ApplicationServices

/// Accessibility: what macOS asks the user to allow before one app may move
/// another's windows, or watch the mouse with a tap that can take events.
/// Zones can do nothing useful without it, so until it is allowed a small
/// window says why and where to turn it on, and notices by itself when that
/// has been done: macOS does not say, so it is asked once a second.
enum Permissions {
    static var isTrusted: Bool {
        AXIsProcessTrusted()
    }

    /// Run once trust arrives, then forgotten.
    private static var waiting: [() -> Void] = []
    private static var timer: Timer?

    private static var window: NSWindow?
    private static var statusIcon: NSImageView?
    private static var statusText: NSTextField?
    private static var actions: OnboardingActions?

    /// At launch: asks macOS, which shows its own prompt the first time,
    /// and with no permission yet opens the onboarding window too.
    static func start() {
        // The prompt option by its value: the imported constant is a global
        // var, which Swift 6 will not let us read from here.
        let options = ["AXTrustedCheckOptionPrompt": true] as CFDictionary
        guard !AXIsProcessTrustedWithOptions(options) else { return }

        watch()
        // A turn of the run loop later, so it lands in front of anything
        // else opening at launch.
        DispatchQueue.main.async { showOnboarding() }
    }

    /// Runs `action` now if Zones is allowed, otherwise the moment it is.
    static func whenTrusted(_ action: @escaping () -> Void) {
        if isTrusted {
            action()
            return
        }
        waiting.append(action)
        watch()
    }

    /// Opens the onboarding window, or brings back the one already open.
    static func showOnboarding() {
        let window = self.window ?? makeWindow()
        if self.window == nil {
            self.window = window
            window.center()
        }
        refreshStatus()
        watch()

        if #available(macOS 14, *) {
            NSApp.activate()
        } else {
            NSApp.activate(ignoringOtherApps: true)
        }
        window.makeKeyAndOrderFront(nil)
    }

    private static func watch() {
        guard timer == nil, !isTrusted else { return }
        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { _ in check() }
    }

    private static func check() {
        guard isTrusted else { return }
        timer?.invalidate()
        timer = nil
        refreshStatus()

        let actions = waiting
        waiting = []
        for action in actions { action() }
    }

    private static func refreshStatus() {
        let trusted = isTrusted
        statusIcon?.isHidden = !trusted
        statusText?.stringValue = trusted ? "Zones is allowed. You can close this window." : "Waiting for permission…"
        statusText?.textColor = trusted ? .labelColor : .secondaryLabelColor
    }

    private static func makeWindow() -> NSWindow {
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 460, height: 260),
                              styleMask: [.titled, .closable, .fullSizeContentView],
                              backing: .buffered, defer: false)
        window.titlebarAppearsTransparent = true
        window.titleVisibility = .hidden
        window.title = "Zones"
        window.isReleasedWhenClosed = false
        window.isMovableByWindowBackground = true

        let icon = NSImageView()
        icon.image = NSApp.applicationIconImage
        icon.imageScaling = .scaleProportionallyUpOrDown
        icon.translatesAutoresizingMaskIntoConstraints = false

        let headline = NSTextField(labelWithString: "Let Zones arrange your windows")
        headline.font = .systemFont(ofSize: 17, weight: .semibold)

        let body = NSTextField(wrappingLabelWithString:
            "macOS asks before an app can move other apps’ windows. Turn on Zones in System Settings → Privacy & Security → Accessibility.")
        body.font = .systemFont(ofSize: 13)
        body.textColor = .secondaryLabelColor
        body.preferredMaxLayoutWidth = 320

        let actions = OnboardingActions()
        let button = NSButton(title: "Open Accessibility Settings", target: actions,
                              action: #selector(OnboardingActions.openAccessibilitySettings))
        button.keyEquivalent = "\r"

        let statusIcon = NSImageView()
        statusIcon.image = NSImage(systemSymbolName: "checkmark.circle.fill", accessibilityDescription: nil)
        statusIcon.contentTintColor = .systemGreen
        let statusText = NSTextField(labelWithString: "")
        statusText.font = .systemFont(ofSize: 12)
        let status = NSStackView(views: [statusIcon, statusText])
        status.orientation = .horizontal
        status.spacing = 6

        let text = NSStackView(views: [headline, body, button, status])
        text.orientation = .vertical
        text.alignment = .leading
        text.spacing = 8
        text.setCustomSpacing(18, after: body)
        text.setCustomSpacing(14, after: button)

        let row = NSStackView(views: [icon, text])
        row.orientation = .horizontal
        row.alignment = .top
        row.spacing = 20
        row.translatesAutoresizingMaskIntoConstraints = false

        // Full-size content: the top inset clears the traffic lights.
        let content = NSView()
        content.addSubview(row)
        NSLayoutConstraint.activate([
            icon.widthAnchor.constraint(equalToConstant: 64),
            icon.heightAnchor.constraint(equalToConstant: 64),
            body.widthAnchor.constraint(equalToConstant: 320),
            row.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 28),
            row.trailingAnchor.constraint(lessThanOrEqualTo: content.trailingAnchor, constant: -28),
            row.topAnchor.constraint(equalTo: content.topAnchor, constant: 44),
        ])
        window.contentView = content

        self.statusIcon = statusIcon
        self.statusText = statusText
        self.actions = actions
        return window
    }
}

/// The onboarding button's target: a selector needs an object to land on.
private final class OnboardingActions: NSObject {
    @objc func openAccessibilitySettings() {
        guard let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")
        else { return }
        NSWorkspace.shared.open(url)
    }
}
