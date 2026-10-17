import AppKit
import ApplicationServices

/// One window of another app, as Accessibility sees it: what Zones reads a
/// frame from and moves.
///
/// Two references to the same window compare equal however each was found
/// (under the cursor, as the focused window, in an app's window list): AX
/// hands out a fresh object every time, and CFEqual is what knows they are
/// the same window. That is what lets a window found by a later drag find
/// the zone an earlier shortcut put it in.
///
/// Every call here goes to the window's app and waits for it to answer. A
/// hung app would hold Zones up for seconds, and Zones' event tap with it,
/// so each element is given a short timeout: a call that runs out fails
/// like any other and the caller leaves that window alone.
struct WindowRef: Hashable {
    let element: AXUIElement
    let pid: pid_t

    init(_ element: AXUIElement, pid: pid_t) {
        AXUIElementSetMessagingTimeout(element, WindowRef.timeout)
        self.element = element
        self.pid = pid
    }

    static func == (a: WindowRef, b: WindowRef) -> Bool {
        CFEqual(a.element, b.element)
    }

    func hash(into hasher: inout Hasher) {
        hasher.combine(CFHash(element))
    }

    // MARK: - Reading and moving

    /// Where the window is and how big, in y-down global points. Nil when it
    /// has gone, or its app did not answer in time.
    var frame: CGRect? {
        guard let origin = WindowRef.point(element, kAXPositionAttribute),
              let size = WindowRef.size(element, kAXSizeAttribute) else { return nil }
        return CGRect(origin: origin, size: size)
    }

    /// Moves and sizes the window. Size first, then position, then size
    /// again: an app clamps a new size to the display the window is on at
    /// that moment, so a window going to a bigger display would otherwise
    /// arrive with the smaller one's limit.
    func setFrame(_ frame: CGRect) {
        setSize(frame.size)
        setPosition(frame.origin)
        setSize(frame.size)
    }

    /// Whether its size can be set at all: a fixed-size dialog cannot be
    /// made to fit a zone.
    var isResizable: Bool {
        var settable: DarwinBoolean = false
        return AXUIElementIsAttributeSettable(element, kAXSizeAttribute as CFString, &settable) == .success
            && settable.boolValue
    }

    var isMinimized: Bool {
        WindowRef.copy(element, kAXMinimizedAttribute) as? Bool ?? false
    }

    /// In a full-screen Space of its own, where a zone means nothing.
    var isFullScreen: Bool {
        WindowRef.copy(element, "AXFullScreen") as? Bool ?? false
    }

    /// A document or app window, as opposed to a palette, a sheet or a popover.
    var isStandardWindow: Bool {
        WindowRef.copy(element, kAXSubroleAttribute) as? String == kAXStandardWindowSubrole
    }

    /// False once the window has closed or its app has quit. An app that is
    /// only slow to answer still counts as there.
    var isValid: Bool {
        var value: CFTypeRef?
        return AXUIElementCopyAttributeValue(element, kAXRoleAttribute as CFString, &value) != .invalidUIElement
    }

    private func setSize(_ size: CGSize) {
        var size = size
        guard let value = AXValueCreate(.cgSize, &size) else { return }
        AXUIElementSetAttributeValue(element, kAXSizeAttribute as CFString, value)
    }

    private func setPosition(_ origin: CGPoint) {
        var origin = origin
        guard let value = AXValueCreate(.cgPoint, &origin) else { return }
        AXUIElementSetAttributeValue(element, kAXPositionAttribute as CFString, value)
    }

    // MARK: - Finding windows

    /// How long any one call may wait for an app, in seconds.
    private static let timeout: Float = 0.25

    private static let ownPID = ProcessInfo.processInfo.processIdentifier

    /// The element hit tests are asked of. Setting its timeout sets the
    /// default for every element, which also covers the hit test itself:
    /// that one is answered by whichever app is under the point.
    private static let systemWide: AXUIElement = {
        let element = AXUIElementCreateSystemWide()
        AXUIElementSetMessagingTimeout(element, timeout)
        return element
    }()

    /// The window under a y-down global point, other than Zones' own. What
    /// is under the point is usually something inside the window (the title
    /// text, a button, a web view), so this climbs from there to the window.
    static func at(_ point: CGPoint) -> WindowRef? {
        var hit: AXUIElement?
        guard AXUIElementCopyElementAtPosition(systemWide, Float(point.x), Float(point.y), &hit) == .success,
              var element = hit else { return nil }

        var pid: pid_t = 0
        guard AXUIElementGetPid(element, &pid) == .success, pid != ownPID else { return nil }

        // Bounded, so a parent chain that loops back on itself cannot hang us.
        for _ in 0..<64 {
            if copy(element, kAXRoleAttribute) as? String == kAXWindowRole {
                return WindowRef(element, pid: pid)
            }
            if let window = asElement(copy(element, kAXWindowAttribute)),
               copy(window, kAXRoleAttribute) as? String == kAXWindowRole {
                return WindowRef(window, pid: pid)
            }
            guard let parent = asElement(copy(element, kAXParentAttribute)) else { return nil }
            element = parent
        }
        return nil
    }

    /// The frontmost app's focused window: what the shortcuts act on. Nil
    /// when Zones itself is in front, so trying out a shortcut in the
    /// settings window does not throw the settings window into a zone.
    static func focused() -> WindowRef? {
        guard let app = NSWorkspace.shared.frontmostApplication, app.processIdentifier != ownPID else { return nil }
        let pid = app.processIdentifier
        guard let window = asElement(copy(application(pid), kAXFocusedWindowAttribute)) else { return nil }
        return WindowRef(window, pid: pid)
    }

    /// Every ordinary window whose middle is on `display`: standard windows
    /// of regular apps, not minimized. What a size flip looks through for
    /// windows sitting in a zone that Zones did not put there.
    static func windows(on display: Display) -> [WindowRef] {
        var list: [WindowRef] = []
        for app in NSWorkspace.shared.runningApplications
        where app.activationPolicy == .regular && app.processIdentifier != ownPID {
            let pid = app.processIdentifier
            guard let elements = copy(application(pid), kAXWindowsAttribute) as? [AXUIElement] else { continue }
            for element in elements {
                let window = WindowRef(element, pid: pid)
                guard window.isStandardWindow, !window.isMinimized, let frame = window.frame,
                      display.frame.contains(CGPoint(x: frame.midX, y: frame.midY)) else { continue }
                list.append(window)
            }
        }
        return list
    }

    // MARK: - Plumbing

    private static func application(_ pid: pid_t) -> AXUIElement {
        let app = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(app, timeout)
        return app
    }

    private static func copy(_ element: AXUIElement, _ attribute: String) -> CFTypeRef? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attribute as CFString, &value) == .success else { return nil }
        return value
    }

    /// A value that is an element, checked by its type rather than assumed.
    private static func asElement(_ value: CFTypeRef?) -> AXUIElement? {
        guard let value = value, CFGetTypeID(value) == AXUIElementGetTypeID() else { return nil }
        return (value as! AXUIElement)
    }

    private static func point(_ element: AXUIElement, _ attribute: String) -> CGPoint? {
        guard let value = copy(element, attribute), CFGetTypeID(value) == AXValueGetTypeID() else { return nil }
        var point = CGPoint.zero
        return AXValueGetValue(value as! AXValue, .cgPoint, &point) ? point : nil
    }

    private static func size(_ element: AXUIElement, _ attribute: String) -> CGSize? {
        guard let value = copy(element, attribute), CFGetTypeID(value) == AXValueGetTypeID() else { return nil }
        var size = CGSize.zero
        return AXValueGetValue(value as! AXValue, .cgSize, &size) ? size : nil
    }
}
