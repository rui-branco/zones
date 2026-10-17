import AppKit
import SwiftUI
import ZonesCore

/// The snapping options: a card of rows, each a title beside its control.
/// The two switches say what they do in a line under the title; the rest are
/// plain enough without. Everything under "Snap windows" only means anything
/// while snapping is on, so with it off those rows fade and their controls
/// stop taking input.
struct SnappingCard: View {
    @ObservedObject var model: SettingsModel

    /// A row with a line of description under its title.
    static let tallRow: CGFloat = 52
    /// A row with a title only.
    static let row: CGFloat = 40

    static let height: CGFloat = 2 * tallRow + CGFloat(1 + SettingsModel.shortcutSlots.count) * row

    var body: some View {
        let on = model.config.snapEnabled
        return VStack(spacing: 0) {
            OptionRow(title: "Snap windows", detail: "Drop dragged windows into zones", dim: false) {
                Toggle("", isOn: Binding(get: { model.config.snapEnabled }, set: { model.setSnapEnabled($0) }))
                    .labelsHidden()
                    .toggleStyle(.switch)
                    .controlSize(.small)
            }
            OptionRow(title: "Hold ⇧ to snap", detail: "Off: every drag shows zones", dim: !on) {
                Toggle("", isOn: Binding(get: { model.config.holdShift }, set: { model.setHoldShift($0) }))
                    .labelsHidden()
                    .toggleStyle(.switch)
                    .controlSize(.small)
                    .disabled(!on)
            }
            .overlay(alignment: .top) { divider }
            OptionRow(title: "Spacing", dim: !on) {
                spacing(on)
            }
            .overlay(alignment: .top) { divider }
            ForEach(SettingsModel.shortcutSlots, id: \.self) { slot in
                shortcutRow(slot, on: on)
                    .overlay(alignment: .top) { divider }
            }
        }
        .background(Card())
    }

    /// The line under the card while shortcuts are taken by another app, as
    /// they then do nothing until changed; nil otherwise.
    static func footnote(_ taken: [Hotkeys.Slot]) -> String? {
        switch taken.count {
        case 0: return nil
        case 1: return "\(SettingsModel.title(taken[0])) shortcut is already in use"
        default: return "\(taken.count) shortcuts are already in use"
        }
    }

    /// The line between two rows, inset from the card's edges.
    private var divider: some View {
        Rectangle()
            .fill(Color.hairline)
            .frame(height: 1)
            .padding(.horizontal, 16)
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

    private func shortcutRow(_ slot: Hotkeys.Slot, on: Bool) -> some View {
        let title = SettingsModel.title(slot)
        let recording = model.recording == slot
        let shortcut = model.shortcut(slot)
        let width = min(ShortcutRecorderView.width(for: shortcut, recording: recording,
                                                   held: recording ? model.heldModifiers : 0),
                        SnappingCard.recorderRoom(beside: title))
        return OptionRow(title: title, dim: !on) {
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
    /// keeps it. Recording with every modifier held, the prompt gives way.
    private static func recorderRoom(beside title: String) -> CGFloat {
        let titleWidth = NSAttributedString(string: title, attributes: [.font: NSFont.systemFont(ofSize: 13)]).size().width
        return Metrics.leftWidth - 2 * 16 - 8 - ceil(titleWidth) - 2
    }
}

/// One row of the card: the words on the left, cut short rather than
/// wrapped if they ever meet the control on the right.
struct OptionRow<Control: View>: View {
    let title: String
    var detail: String? = nil
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
                if let detail {
                    Text(detail)
                        .font(.system(size: 11))
                        .foregroundStyle(dim ? Color.textFaint : Color.textDim)
                }
            }
            .lineLimit(1)
            .truncationMode(.tail)
            Spacer(minLength: 8)
            control()
                .fixedSize()
                .layoutPriority(1)
        }
        .padding(.horizontal, 16)
        .frame(height: detail == nil ? SnappingCard.row : SnappingCard.tallRow)
    }
}
