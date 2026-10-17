import AppKit

let arguments = CommandLine.arguments

// `Zones --render-settings <path.png> [--dark]` draws the settings window to
// a PNG and quits, without showing anything: for checking the design over
// ssh, where there is no one to look at a window.
if let flag = arguments.firstIndex(of: "--render-settings") {
    guard flag + 1 < arguments.count else {
        FileHandle.standardError.write(Data("usage: Zones --render-settings <path.png> [--dark]\n".utf8))
        exit(1)
    }
    NSApplication.shared.setActivationPolicy(.prohibited)
    let ok = SettingsWindow.renderSnapshot(to: arguments[flag + 1], dark: arguments.contains("--dark"))
    exit(ok ? 0 : 1)
}

// A menu bar app: no Dock icon, no main window.
let app = NSApplication.shared
app.setActivationPolicy(.accessory)
let delegate = AppDelegate()
app.delegate = delegate
app.run()
