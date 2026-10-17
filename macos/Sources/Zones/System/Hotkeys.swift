import Carbon
import Foundation
import ZonesCore

/// Zones' global shortcuts: next and previous zone, next and previous size,
/// each its own shortcut. Carbon's RegisterEventHotKey, which needs no
/// permission and tells us when another app already owns a combination.
///
/// CONTRACT: the settings window calls apply/suspend/resume and reads failed;
/// the engine sets onPress.
///
/// One registration per slot, id slot+1, as the Windows version numbers them.
/// The presses come through one Carbon handler on the application's event
/// target, which the main run loop dispatches, so nothing here runs anywhere
/// but the main thread.
final class Hotkeys {
    enum Slot: Int, CaseIterable {
        case zone = 0
        case size = 1
        case zoneBack = 2
        case sizeBack = 3

        /// Which way the slot goes: +1 next, -1 previous.
        var step: Int { self == .zoneBack || self == .sizeBack ? -1 : 1 }
    }

    static let shared = Hotkeys()

    /// A shortcut fired: its slot, and the slot's step (+1 next, -1 previous). Main thread.
    var onPress: ((Slot, Int) -> Void)?

    /// Slots whose shortcut another app already owns.
    private(set) var failed: Set<Slot> = []

    /// "ZONS": marks a hot key event as one of ours.
    private static let signature: OSType = 0x5A4F_4E53

    /// What the last apply asked for, kept through a suspend so resume can
    /// put it back. A slot with no entry has no shortcut.
    private var shortcuts: [Slot: Shortcut] = [:]

    private var registered: [UInt32: EventHotKeyRef] = [:]
    private var handler: EventHandlerRef?
    private var suspended = false
    private var observer: NSObjectProtocol?

    private init() {
        // Settings change live; the shortcuts follow every save. Not while a
        // new one is being recorded, though: its keys must reach the recorder.
        observer = NotificationCenter.default.addObserver(
            forName: .zonesConfigChanged, object: nil, queue: .main
        ) { [weak self] _ in
            guard let self = self else { return }
            let config = AppState.shared.config
            if self.suspended {
                self.remember(config)
            } else {
                self.apply(config)
            }
        }
    }

    /// Registers the config's shortcuts, replacing any before; none while
    /// snapping is off. False if any of them is taken (see `failed`).
    @discardableResult
    func apply(_ config: Config) -> Bool {
        remember(config)
        suspended = false
        return register()
    }

    /// Lets go of every shortcut while a new one is being recorded, so the
    /// keys reach the recorder instead of firing.
    func suspend() {
        suspended = true
        unregisterAll()
    }

    /// Takes the shortcuts back after `suspend()`.
    @discardableResult
    func resume() -> Bool {
        suspended = false
        return register()
    }

    private func remember(_ config: Config) {
        shortcuts = [:]
        guard config.snapEnabled else { return }
        shortcuts[.zone] = config.zoneShortcut
        shortcuts[.size] = config.sizeShortcut
        shortcuts[.zoneBack] = config.zoneBackShortcut
        shortcuts[.sizeBack] = config.sizeBackShortcut
    }

    private func register() -> Bool {
        unregisterAll()
        failed = []
        installHandler()

        for slot in Slot.allCases {
            guard let shortcut = shortcuts[slot] else { continue }
            if !add(UInt32(slot.rawValue + 1), shortcut.keyCode, shortcut.modifiers) {
                failed.insert(slot)
            }
        }
        return failed.isEmpty
    }

    private func add(_ id: UInt32, _ keyCode: UInt32, _ modifiers: UInt32) -> Bool {
        var ref: EventHotKeyRef?
        let status = RegisterEventHotKey(keyCode, modifiers, EventHotKeyID(signature: Hotkeys.signature, id: id),
                                         GetApplicationEventTarget(), 0, &ref)
        guard status == OSStatus(noErr), let ref = ref else { return false }
        registered[id] = ref
        return true
    }

    private func unregisterAll() {
        for ref in registered.values { UnregisterEventHotKey(ref) }
        registered = [:]
    }

    /// Once: one handler serves every registration, told apart by id.
    private func installHandler() {
        guard handler == nil else { return }
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, event, _ -> OSStatus in
            var id = EventHotKeyID()
            guard let event = event,
                  GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                                    nil, MemoryLayout<EventHotKeyID>.size, nil, &id) == OSStatus(noErr),
                  id.signature == Hotkeys.signature, id.id >= 1,
                  let slot = Slot(rawValue: Int(id.id - 1)) else { return OSStatus(eventNotHandledErr) }

            // Handed on after the handler returns, so whatever the press does
            // (an alert, a window) runs outside Carbon's dispatch.
            DispatchQueue.main.async { Hotkeys.shared.onPress?(slot, slot.step) }
            return OSStatus(noErr)
        }, 1, &spec, nil, &handler)
    }
}
