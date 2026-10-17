import AppKit
import SwiftUI
import ZonesCore

/// The snapping options: one card of rows, each a title and a line on what it
/// does beside its control. Everything under "Snap windows" only means
/// anything while snapping is on, so with it off those rows fade and their
/// controls stop taking input.
struct SnappingPage: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        let on = model.config.snapEnabled
        return VStack(alignment: .leading, spacing: 8) {
            VStack(spacing: 0) {
                OptionRow(title: "Snap windows", detail: "Drop a dragged window into a zone", dim: false) {
                    Toggle("", isOn: Binding(get: { model.config.snapEnabled }, set: { model.setSnapEnabled($0) }))
                        .labelsHidden()
                        .toggleStyle(.switch)
                        .controlSize(.small)
                }
                OptionRow(title: "Hold ⇧ to snap", detail: "Zones show only while ⇧ is held. Off, every drag shows them",
                          dim: !on) {
                    Toggle("", isOn: Binding(get: { model.config.holdShift }, set: { model.setHoldShift($0) }))
                        .labelsHidden()
                        .toggleStyle(.switch)
                        .controlSize(.small)
                        .disabled(!on)
                }
                .overlay(alignment: .top) { RowDivider() }
                OptionRow(title: "Spacing", detail: "The gap around each zone", dim: !on) {
                    spacing(on)
                }
                .overlay(alignment: .top) { RowDivider() }
            }
            .background(Card())

            // The one thing about snapping with no control of its own.
            Text("While zones are showing, scroll to switch between the display's sizes.")
                .font(.system(size: 11))
                .foregroundStyle(Color.textDim)
                .lineLimit(1)
        }
    }

    private func spacing(_ on: Bool) -> some View {
        HStack(spacing: 4) {
            Text("\(model.config.spacing) pt")
                .font(.system(size: 12))
                .monospacedDigit()
                .foregroundStyle(on ? Color.text : Color.textFaint)
            Stepper("", value: Binding(get: { model.config.spacing }, set: { model.setSpacing($0) }),
                    in: 0...Config.maxSpacing, step: 2)
                .labelsHidden()
                .controlSize(.small)
        }
        .disabled(!on)
    }
}

/// The shortcuts: one card, a row for each, its recorder on the right. They
/// only work while snapping is on, so with it off the rows fade and the
/// recorders stop taking clicks. Under the card, how to set one, or, while
/// another app has some of them, which.
struct ShortcutsPage: View {
    @ObservedObject var model: SettingsModel
    /// The card's: the recorders get what the titles leave of it.
    let width: CGFloat

    var body: some View {
        let on = model.config.snapEnabled
        let warning = ShortcutsPage.warning(model.takenSlots)
        return VStack(alignment: .leading, spacing: 8) {
            VStack(spacing: 0) {
                ForEach(SettingsModel.shortcutSlots, id: \.self) { slot in
                    row(slot, on: on)
                        .overlay(alignment: .top) {
                            if slot != SettingsModel.shortcutSlots.first { RowDivider() }
                        }
                }
            }
            .background(Card())

            Text(warning ?? "Click a shortcut, then press the keys for it. Delete clears it.")
                .font(.system(size: 11))
                .foregroundStyle(warning == nil ? Color.textDim : Color.danger)
                .lineLimit(1)
        }
    }

    /// The line under the card while shortcuts are taken by another app, as
    /// they then do nothing until changed; nil otherwise.
    static func warning(_ taken: [Hotkeys.Slot]) -> String? {
        switch taken.count {
        case 0: return nil
        case 1: return "\(SettingsModel.title(taken[0])) shortcut is already in use"
        default: return "\(taken.count) shortcuts are already in use"
        }
    }

    /// What a shortcut does, under its title.
    private static func detail(_ slot: Hotkeys.Slot) -> String {
        switch slot {
        case .zone: return "Moves the active window to its next zone"
        case .zoneBack: return "Moves it back to the zone before"
        case .size: return "Switches the display to its next size, its windows with it"
        case .sizeBack: return "Switches back to the size before"
        }
    }

    private func row(_ slot: Hotkeys.Slot, on: Bool) -> some View {
        let title = SettingsModel.title(slot)
        let recording = model.recording == slot
        let shortcut = model.shortcut(slot)
        let width = min(ShortcutRecorderView.width(for: shortcut, recording: recording,
                                                   held: recording ? model.heldModifiers : 0),
                        recorderRoom(beside: title))
        return OptionRow(title: title, detail: ShortcutsPage.detail(slot), dim: !on) {
            ShortcutField(
                shortcut: shortcut,
                enabled: on,
                onChange: { model.setShortcut($0, for: slot) },
                onRecordingChange: { model.recordingChanged(slot, recording: $0, held: $1) })
                .frame(width: width, height: ShortcutRecorderView.height)
        }
    }

    /// The most a recorder may take: the row less its padding and its title,
    /// so the title always shows whole, as the Windows box's MaximumSize
    /// keeps it; the line under it is cut short first. Recording with every
    /// modifier held in a narrow window, the prompt gives way.
    private func recorderRoom(beside title: String) -> CGFloat {
        let titleWidth = NSAttributedString(string: title, attributes: [.font: NSFont.systemFont(ofSize: 13)]).size().width
        return width - 2 * 16 - 8 - ceil(titleWidth) - 2
    }
}

/// Zones itself: starting at login and updating in one card, the version and
/// the updates in another, the update button saying what the updater is doing.
struct GeneralPage: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        VStack(spacing: Metrics.cardGap) {
            VStack(spacing: 0) {
                OptionRow(title: "Start at Login", detail: "Opens Zones in the menu bar when you log in", dim: false) {
                    Toggle("", isOn: Binding(get: { model.startsAtLogin }, set: { model.setStartAtLogin($0) }))
                        .labelsHidden()
                        .toggleStyle(.switch)
                        .controlSize(.small)
                }
                OptionRow(title: "Auto-update", detail: "Installs new versions by itself while Zones is idle", dim: false) {
                    Toggle("", isOn: Binding(get: { model.config.autoUpdate }, set: { model.setAutoUpdate($0) }))
                        .labelsHidden()
                        .toggleStyle(.switch)
                        .controlSize(.small)
                }
                .overlay(alignment: .top) { RowDivider() }
            }
            .background(Card())

            OptionRow(title: "Version \(SettingsView.version)", detail: updateDetail, dim: false) {
                updateButton
            }
            .background(Card())
        }
    }

    /// What the updater is doing, in words, under the version.
    private var updateDetail: String {
        switch model.update {
        case .checking: return "Checking for updates…"
        case .downloading(let release), .installing(let release): return "Installing Zones \(release.version)…"
        case .available(let release): return "Zones \(release.version) is available"
        case .idle, .failed: return "Zones checks for updates every few hours"
        }
    }

    /// What the updater is doing, as a button: a check to start, then the
    /// update it found in the accent colour, the one thing on the page
    /// waiting on the user. Greyed out, saying so, while it is busy.
    @ViewBuilder
    private var updateButton: some View {
        switch model.update {
        case .idle, .failed:
            Button {
                model.checkForUpdates()
            } label: {
                Label("Check for Updates", systemImage: "arrow.triangle.2.circlepath")
            }
            .buttonStyle(.bordered)
            .controlSize(.small)
        case .checking:
            Button {} label: {
                Label("Checking…", systemImage: "arrow.triangle.2.circlepath")
            }
            .buttonStyle(.bordered)
            .controlSize(.small)
            .disabled(true)
        case .available(let release):
            Button {
                model.installUpdate()
            } label: {
                Label("Update to \(release.version)", systemImage: "arrow.down.circle")
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.small)
        case .downloading, .installing:
            Button {} label: {
                Label("Updating…", systemImage: "arrow.down.circle")
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.small)
            .disabled(true)
        }
    }
}

/// One row of a card: its title, and under it a line on what it does, on the
/// left, cut short rather than wrapped if they ever meet the control on the
/// right.
struct OptionRow<Control: View>: View {
    let title: String
    let detail: String
    let dim: Bool
    @ViewBuilder let control: () -> Control

    var body: some View {
        // The gap is the spacer's own: HStack spacing would go in on both
        // sides of it and take twice that from the words.
        HStack(spacing: 0) {
            VStack(alignment: .leading, spacing: 1) {
                Text(title)
                    .font(.system(size: 13))
                    .foregroundStyle(dim ? Color.textFaint : Color.text)
                Text(detail)
                    .font(.system(size: 11))
                    .foregroundStyle(dim ? Color.textFaint : Color.textDim)
            }
            .lineLimit(1)
            .truncationMode(.tail)
            Spacer(minLength: 8)
            control()
                .fixedSize()
                .layoutPriority(1)
        }
        .padding(.horizontal, 16)
        .frame(height: Metrics.rowHeight)
    }
}

/// The line between two rows of a card, inset from the card's edges.
struct RowDivider: View {
    var body: some View {
        Rectangle()
            .fill(Color.hairline)
            .frame(height: 1)
            .padding(.horizontal, 16)
    }
}
