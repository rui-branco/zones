import Foundation
import ZonesCore

extension Notification.Name {
    /// The config changed and was saved: shortcuts re-register, an open
    /// settings window refreshes.
    static let zonesConfigChanged = Notification.Name("ZonesConfigChanged")
}

/// The one config the whole app shares. Main thread only, like Config itself.
final class AppState {
    static let shared = AppState()

    let config: Config

    private init() {
        config = Config.load()
    }

    /// Saves the config and tells everyone who cares.
    func save() {
        config.save()
        NotificationCenter.default.post(name: .zonesConfigChanged, object: nil)
    }
}
