import AppKit
import CryptoKit
import Darwin
import ZonesCore

extension Notification.Name {
    /// Updater.shared.state changed. Posted on the main thread: the menu
    /// and an open settings window show the new state.
    static let zonesUpdateChanged = Notification.Name("ZonesUpdateChanged")
}

/// Zones' own updates, from the releases published on GitHub (UpdateCheck).
/// Main thread only, like the rest of the app.
///
/// A quiet check a minute after launch and every six hours. What it finds
/// newer installs by itself with the autoUpdate setting on: downloaded and
/// checked in the background, then swapped in once Zones is not in use
/// (isIdle), and opened with --background so nothing shows. With the setting
/// off it is only offered, in the menu and the settings window. The menu's
/// Check for Updates… says what it found in an alert, and installs at once
/// when asked to.
///
/// Only a copy that is an app straight in /Applications or ~/Applications
/// replaces itself: it downloads the zip and SHA256SUMS.txt, checks the one
/// against the other, unzips it, checks the app in it, and quits for a
/// script of its own (swapScript) to swap the new app in and open it. Any
/// other copy opens the release's page instead, when asked to install.
final class Updater {
    static let shared = Updater()

    enum State: Equatable {
        case idle
        case checking
        case available(Release)
        case downloading
        case installing
        case failed(String)
    }

    private(set) var state: State = .idle {
        didSet {
            if state != oldValue { NotificationCenter.default.post(name: .zonesUpdateChanged, object: self) }
        }
    }

    /// This copy's version; 0.0.0 for a build run outside its bundle, which
    /// has none.
    let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0"

    private static let firstCheckDelay: TimeInterval = 60
    private static let checkInterval: TimeInterval = 6 * 60 * 60

    /// How often an automatic install waiting for Zones to be idle looks again.
    private static let idlePoll: TimeInterval = 30

    /// The most a download may be: Zones-mac.zip is a few MB.
    fileprivate static let maxDownload: Int64 = 300 * 1024 * 1024

    /// The copy an update replaced, beside the new one until it is deleted.
    private static let oldName = ".Zones-old.app"

    /// Opened with this, Zones shows nothing at launch: the swap script
    /// opens the new copy with it after an automatic install.
    static let backgroundArgument = "--background"

    /// Followed by a version, the one the swap script could not install
    /// when it opened this copy again.
    private static let failedArgument = "--update-failed"

    /// The version that did not install, this run or (from failedArgument)
    /// the one before: not installed by itself again until Zones is opened
    /// anew. Offered all the same.
    private var failedVersion: String?

    private var timer: Timer?

    private let session = URLSession(configuration: Updater.configuration)

    private init() {
        let arguments = CommandLine.arguments
        if let flag = arguments.firstIndex(of: Updater.failedArgument), flag + 1 < arguments.count {
            failedVersion = arguments[flag + 1]
            NSLog("Zones: the update to %@ did not install; not trying it again by itself", arguments[flag + 1])
        }
    }

    fileprivate static var configuration: URLSessionConfiguration {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.httpShouldSetCookies = false
        configuration.httpCookieAcceptPolicy = .never
        configuration.httpCookieStorage = nil
        configuration.urlCache = nil
        configuration.timeoutIntervalForRequest = 60
        configuration.timeoutIntervalForResource = 15 * 60
        return configuration
    }

    /// The first check a minute from now, then one every six hours. The copy
    /// an update replaced goes then too: the swap script keeps it until this
    /// one has been running 5 s, to put back if it does not start.
    func start() {
        guard timer == nil else { return }
        let timer = Timer(fire: Date().addingTimeInterval(Updater.firstCheckDelay), interval: Updater.checkInterval,
                          repeats: true) { [weak self] _ in
            self?.check(manual: false)
        }
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer

        DispatchQueue.main.asyncAfter(deadline: .now() + Updater.firstCheckDelay) {
            Updater.removeOldCopy()
        }
    }

    /// The autoUpdate setting changed: turned on, an update on offer installs.
    func autoUpdateChanged() {
        if case .available(let release) = state { installAutomatically(release) }
    }

    // MARK: - Checking

    /// Asks GitHub for the newest release. `manual`, from the menu, says what
    /// it found in an alert. Not while a check or an install is under way;
    /// with an update on offer, only a quiet check looks again, for a newer
    /// one, leaving the offer as it is meanwhile.
    func check(manual: Bool) {
        switch state {
        case .idle, .failed: state = .checking
        case .available where !manual: break
        case .checking, .available, .downloading, .installing: return
        }

        var request = URLRequest(url: UpdateCheck.latestURL)
        for (field, value) in UpdateCheck.headers {
            request.setValue(value, forHTTPHeaderField: field)
        }
        session.dataTask(with: request) { data, response, error in
            let found = Updater.latest(data, response, error)
            DispatchQueue.main.async { self.checked(found, manual: manual) }
        }.resume()
    }

    /// GitHub's answer as the newest release with the Mac app in it, nil for
    /// none: a 404 is nothing released yet, and a release with no
    /// Zones-mac.zip is not one for this app.
    private static func latest(_ data: Data?, _ response: URLResponse?, _ error: Error?) -> Result<Release?, Failure> {
        if let error { return .failure(Failure(error.localizedDescription)) }
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        if status == 404 { return .success(nil) }
        guard status == 200, let data else { return .failure(Failure("GitHub answered with status \(status).")) }
        return .success(UpdateCheck.release(from: data))
    }

    private func checked(_ found: Result<Release?, Failure>, manual: Bool) {
        // An install started meanwhile, from the offer: it carries on.
        switch state {
        case .checking, .available: break
        case .idle, .downloading, .installing, .failed: return
        }

        switch found {
        case .failure(let failure):
            // Looking again at an update on offer: it stays on offer.
            if case .checking = state { state = .failed(failure.message) }
            if manual { Updater.tell("Could not check for updates.", failure.message, style: .warning) }
        case .success(let release?) where UpdateCheck.isNewer(release.version, than: version):
            state = .available(release)
            if manual {
                offer()
            } else {
                installAutomatically(release)
            }
        case .success:
            state = .idle
            if manual { Updater.tell("Zones \(version) is the latest version.", "", style: .informational) }
        }
    }

    // MARK: - Installing

    /// Asks whether to install the update on offer, and installs it if so.
    func offer() {
        guard case .available(let release) = state else { return }
        // A copy outside Applications cannot replace itself; it can only
        // send the user to the download, so the button says that.
        let inPlace = Updater.replaceableBundle != nil
        let alert = NSAlert()
        alert.messageText = "Zones \(release.version) is available."
        alert.informativeText = inPlace
            ? "Install it and relaunch?"
            : "This copy of Zones is not in Applications, so it cannot update itself. Download the new version from GitHub?"
        alert.alertStyle = .informational
        alert.addButton(withTitle: inPlace ? "Install and Relaunch" : "Download")
        alert.addButton(withTitle: "Later")
        Updater.activate()
        if alert.runModal() == .alertFirstButtonReturn { install() }
    }

    /// Installs the update on offer and relaunches, at once, as the user
    /// asked: downloaded, checked and swapped in when this copy can replace
    /// itself; otherwise its release page is opened, to download it there.
    func install() {
        guard case .available(let release) = state else { return }
        guard let app = Updater.replaceableBundle else {
            NSWorkspace.shared.open(release.pageURL)
            return
        }
        download(release, replacing: app, automatic: false)
    }

    /// With the autoUpdate setting on, in a copy that can replace itself,
    /// and not the version that did not install: downloaded now, installed
    /// once Zones is idle.
    private func installAutomatically(_ release: Release) {
        guard AppState.shared.config.autoUpdate, release.version != failedVersion,
              let app = Updater.replaceableBundle
        else { return }
        download(release, replacing: app, automatic: true)
    }

    /// This copy's bundle when it may replace itself: an .app straight in
    /// /Applications or ~/Applications, where it was put to stay. Nil for a
    /// build being tried out from anywhere else.
    private static var replaceableBundle: URL? {
        let bundle = Bundle.main.bundleURL.standardizedFileURL
        guard bundle.pathExtension == "app" else { return nil }
        let folder = bundle.deletingLastPathComponent().path
        let home = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Applications").path
        return folder == "/Applications" || folder == home ? bundle : nil
    }

    /// ~/Library/Caches/Zones/updates: a folder for each version downloaded,
    /// the whole of it cleared before each download.
    private static var folder: URL {
        FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Zones/updates", isDirectory: true)
    }

    /// Downloads and checks `release` off the main thread, then swaps it in
    /// for `app`: at once when the user asked, once Zones is idle when not.
    private func download(_ release: Release, replacing app: URL, automatic: Bool) {
        state = .downloading
        let folder = Updater.folder.appendingPathComponent(release.version, isDirectory: true)
        Task {
            do {
                try Updater.clear(folder)
                try await Download.run(release.sumsURL, to: folder.appendingPathComponent(UpdateCheck.sumsName))
                try await Download.run(release.zipURL, to: folder.appendingPathComponent(UpdateCheck.zipName))
                await MainActor.run { self.state = .installing }
                let new = try await Task.detached { try Updater.unpack(release, in: folder) }.value
                await MainActor.run {
                    if automatic {
                        self.installWhenIdle(release, replacing: app, with: new, in: folder)
                    } else {
                        self.swap(release, replacing: app, with: new, in: folder, automatic: false)
                    }
                }
            } catch {
                let failure = error as? Failure ?? Failure(error.localizedDescription)
                await MainActor.run { self.failed(release, failure, automatic: automatic) }
            }
        }
    }

    private static func clear(_ version: URL) throws {
        let fm = FileManager.default
        if fm.fileExists(atPath: folder.path) { try fm.removeItem(at: folder) }
        try fm.createDirectory(at: version, withIntermediateDirectories: true)
    }

    /// The downloaded zip checked against SHA256SUMS.txt, unzipped beside it,
    /// and the Zones.app in it checked: the release's version, and a
    /// signature that holds. That app.
    private static func unpack(_ release: Release, in folder: URL) throws -> URL {
        let zip = folder.appendingPathComponent(UpdateCheck.zipName)
        let sums = (try? String(contentsOf: folder.appendingPathComponent(UpdateCheck.sumsName), encoding: .utf8)) ?? ""
        guard let expected = UpdateCheck.checksum(for: UpdateCheck.zipName, in: sums) else {
            throw Failure("\(UpdateCheck.sumsName) has no checksum for \(UpdateCheck.zipName).")
        }
        let digest = SHA256.hash(data: try Data(contentsOf: zip, options: .mappedIfSafe))
        let actual = digest.map { String(format: "%02x", $0) }.joined()
        guard actual == expected else {
            throw Failure("The download is damaged: its SHA-256 is not the one in \(UpdateCheck.sumsName).")
        }

        guard run("/usr/bin/ditto", ["-x", "-k", zip.path, folder.path]) == 0 else {
            throw Failure("Could not unzip \(UpdateCheck.zipName).")
        }
        let app = folder.appendingPathComponent("Zones.app", isDirectory: true)
        let info = NSDictionary(contentsOf: app.appendingPathComponent("Contents/Info.plist"))
        let found = info?["CFBundleShortVersionString"] as? String
        guard found == release.version else {
            throw Failure("The downloaded app is version \(found ?? "unknown"), not \(release.version).")
        }
        guard run("/usr/bin/codesign", ["--verify", "--deep", "--strict", app.path]) == 0 else {
            throw Failure("The downloaded app's signature does not verify.")
        }
        return app
    }

    /// `tool` run to the end, its output dropped: its exit status, or -1 if
    /// it did not start.
    private static func run(_ tool: String, _ arguments: [String]) -> Int32 {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: tool)
        process.arguments = arguments
        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        do {
            try process.run()
        } catch {
            return -1
        }
        process.waitUntilExit()
        return process.terminationStatus
    }

    /// Zones not in use: no window being dragged, no zones showing, and the
    /// settings window closed, so no shortcut being recorded in it either.
    private static var isIdle: Bool {
        DragSnapper.shared.draggedWindow == nil && !Overlay.shared.isShowing
            && !SettingsWindow.isOpen && !SettingsWindow.isRecordingShortcut
    }

    /// An automatic install, once Zones is idle, looking again every 30 s
    /// until it is. The setting turned off meanwhile, it is offered instead.
    private func installWhenIdle(_ release: Release, replacing app: URL, with new: URL, in folder: URL) {
        guard AppState.shared.config.autoUpdate else {
            state = .available(release)
            return
        }
        guard Updater.isIdle else {
            DispatchQueue.main.asyncAfter(deadline: .now() + Updater.idlePoll) { [weak self] in
                self?.installWhenIdle(release, replacing: app, with: new, in: folder)
            }
            return
        }
        swap(release, replacing: app, with: new, in: folder, automatic: true)
    }

    /// Writes the swap script beside the download, starts it on its own and
    /// quits, so it can put `new` where `app` is.
    private func swap(_ release: Release, replacing app: URL, with new: URL, in folder: URL, automatic: Bool) {
        let script = folder.appendingPathComponent("install.sh")
        do {
            try Updater.swapScript.write(to: script, atomically: true, encoding: .utf8)
        } catch {
            return failed(release, Failure("Could not write the installer: \(error.localizedDescription)"),
                          automatic: automatic)
        }
        let arguments = ["/bin/sh", script.path, String(getpid()), new.path, app.path,
                         automatic ? "background" : "foreground", release.version]
        guard Updater.spawnDetached(arguments) else {
            return failed(release, Failure("Could not start the installer."), automatic: automatic)
        }
        NSApp.terminate(nil)
    }

    /// Not tried again by itself this run. One the user asked for is a
    /// failure they are told of; an automatic one goes back on offer.
    private func failed(_ release: Release, _ failure: Failure, automatic: Bool) {
        failedVersion = release.version
        if automatic {
            NSLog("Zones: the update to %@ did not install: %@", release.version, failure.message)
            state = .available(release)
        } else {
            state = .failed(failure.message)
            Updater.tell("Could not update Zones.", failure.message, style: .warning)
        }
    }

    /// In a session of its own (setsid), so it outlives this process, whose
    /// leftovers launchd may end when it quits.
    private static func spawnDetached(_ arguments: [String]) -> Bool {
        var attributes: posix_spawnattr_t?
        posix_spawnattr_init(&attributes)
        defer { posix_spawnattr_destroy(&attributes) }
        posix_spawnattr_setflags(&attributes, Int16(POSIX_SPAWN_SETSID))

        let argv = arguments.map { strdup($0) } + [nil]
        defer { argv.forEach { free($0) } }

        var pid: pid_t = 0
        return posix_spawn(&pid, arguments[0], nil, &attributes, argv, environ) == 0
    }

    /// The copy an update replaced, beside this one: deleted once the swap
    /// script is done with it.
    private static func removeOldCopy() {
        let bundle = Bundle.main.bundleURL.standardizedFileURL
        guard bundle.pathExtension == "app" else { return }
        let old = bundle.deletingLastPathComponent().appendingPathComponent(oldName, isDirectory: true)
        if FileManager.default.fileExists(atPath: old.path) {
            try? FileManager.default.removeItem(at: old)
        }
    }

    /// $1 the running Zones' pid, $2 the new Zones.app, $3 the running one's
    /// bundle, $4 "background" after an automatic install (whatever it opens
    /// shows nothing) or "foreground", $5 the version being installed. Its
    /// output goes to install.log beside it.
    private static let swapScript = """
        #!/bin/sh
        # Zones' update: once the running copy has quit, moves it aside to .Zones-old.app,
        # moves the new one into its place and opens it. If that fails, or Zones is not
        # running 5 s later, the old copy goes back and is opened instead, told which
        # version did not install. The new copy deletes .Zones-old.app itself, a minute
        # after it starts.
        exec >>"$(dirname "$0")/install.log" 2>&1
        PATH=/usr/bin:/bin:/usr/sbin:/sbin

        pid="$1"
        new="$2"
        app="$3"
        mode="$4"
        version="$5"
        old="$(dirname "$app")/\(oldName)"

        echo "$(date): installing $version, $new as $app ($mode)"

        launch() {
            if [ "$mode" = background ]; then open "$1" --args \(backgroundArgument); else open "$1"; fi
        }

        # The old copy, told which version did not install, so it does not try it again by itself.
        launch_failed() {
            if [ "$mode" = background ]; then
                open "$1" --args \(backgroundArgument) \(failedArgument) "$version"
            else
                open "$1" --args \(failedArgument) "$version"
            fi
        }

        # While $pid runs, up to $1 tenths of a second.
        wait_for_exit() {
            i=0
            while kill -0 "$pid" 2>/dev/null && [ "$i" -lt "$1" ]; do
                sleep 0.1
                i=$((i + 1))
            done
        }

        # The new copy out, the old one back in its place, and opened.
        restore() {
            echo "$1, putting the old copy back"
            rm -rf "$app"
            if [ ! -e "$app" ] && mv "$old" "$app"; then
                launch_failed "$app"
            else
                echo "could not put $old back, opening it where it is"
                launch_failed "$old"
            fi
            exit 1
        }

        # Up to 15 s for Zones to quit; then it is killed, and given 5 s more.
        wait_for_exit 150
        if kill -0 "$pid" 2>/dev/null; then
            echo "Zones ($pid) still running after 15 s, killing it"
            kill -9 "$pid"
            wait_for_exit 50
        fi
        if kill -0 "$pid" 2>/dev/null; then
            echo "Zones ($pid) will not quit, not installing"
            exit 1
        fi

        if [ ! -x "$new/Contents/MacOS/Zones" ]; then
            echo "no app at $new, opening $app again"
            launch_failed "$app"
            exit 1
        fi

        # Nothing is left where the old copy goes: mv onto a folder would move into it.
        rm -rf "$old"
        if [ -e "$old" ] || ! mv "$app" "$old"; then
            echo "could not move $app aside, opening it again"
            launch_failed "$app"
            exit 1
        fi
        mv "$new" "$app" || restore "could not move $new into place"
        xattr -dr com.apple.quarantine "$app" 2>/dev/null || true
        launch "$app" || restore "could not open $app"

        sleep 5
        if ! pgrep -u "$(id -u)" -f "$app/Contents/MacOS/Zones" >/dev/null; then
            restore "Zones not running 5 s after opening it"
        fi
        echo "installed, Zones running"

        """

    // MARK: - Alerts

    /// Zones has no Dock icon: an alert only comes to the front with Zones
    /// made the active app first.
    private static func activate() {
        if #available(macOS 14, *) {
            NSApp.activate()
        } else {
            NSApp.activate(ignoringOtherApps: true)
        }
    }

    private static func tell(_ message: String, _ detail: String, style: NSAlert.Style) {
        let alert = NSAlert()
        alert.messageText = message
        alert.informativeText = detail
        alert.alertStyle = style
        activate()
        alert.runModal()
    }
}

/// Why a check or an update did not go through, in words for an alert.
private struct Failure: Error {
    let message: String

    init(_ message: String) {
        self.message = message
    }
}

/// One file downloaded, in a session of its own whose delegate this is, so
/// a download is stopped as soon as it passes Updater.maxDownload rather
/// than once all of it is on disk.
private final class Download: NSObject, URLSessionDownloadDelegate {
    private let destination: URL
    private var continuation: CheckedContinuation<Void, Error>?

    // On the session's queue, until the task completes.
    private var failure: String?
    private var moved = false

    private init(destination: URL) {
        self.destination = destination
    }

    /// The file at `url` at `destination`; throws a Failure for anything but
    /// a 200 no bigger than Updater.maxDownload.
    static func run(_ url: URL, to destination: URL) async throws {
        let download = Download(destination: destination)
        let session = URLSession(configuration: Updater.configuration, delegate: download, delegateQueue: nil)
        defer { session.finishTasksAndInvalidate() }

        var request = URLRequest(url: url)
        request.setValue(UpdateCheck.headers["User-Agent"], forHTTPHeaderField: "User-Agent")
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            download.continuation = continuation
            session.downloadTask(with: request).resume()
        }
    }

    private var name: String {
        destination.lastPathComponent
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didWriteData bytesWritten: Int64,
                    totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) {
        guard failure == nil, max(totalBytesWritten, totalBytesExpectedToWrite) > Updater.maxDownload else { return }
        failure = "\(name) is bigger than \(Updater.maxDownload / 1024 / 1024) MB."
        downloadTask.cancel()
    }

    /// The file is deleted once this returns, so it is moved now.
    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        guard failure == nil else { return }
        let status = (downloadTask.response as? HTTPURLResponse)?.statusCode ?? 0
        guard status == 200 else {
            failure = "Downloading \(name), the server answered with status \(status)."
            return
        }
        do {
            try FileManager.default.moveItem(at: location, to: destination)
            moved = true
        } catch {
            failure = "Could not keep \(name): \(error.localizedDescription)"
        }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        if failure == nil, let error { failure = error.localizedDescription }
        if failure == nil, !moved { failure = "\(name) did not download." }

        if let failure {
            continuation?.resume(throwing: Failure(failure))
        } else {
            continuation?.resume()
        }
        continuation = nil
    }
}
