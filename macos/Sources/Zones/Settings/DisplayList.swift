import AppKit
import SwiftUI

/// The displays down the side of the window, one row each, the chosen one
/// marked with an accent pill at its edge, like a sidebar's selection.
///
/// Each row gives the display's name and its size and whether it is the main
/// one: what tells two of the same make apart in System Settings as well.
struct DisplayList: View {
    @ObservedObject var model: SettingsModel
    @State private var hover: String?

    /// All the rows, but never more than `room`: the snapping card under the
    /// list must not run off the bottom of the window, so past that it scrolls.
    static func height(for count: Int, room: CGFloat) -> CGFloat {
        guard count > 0 else { return 0 }
        let rows = CGFloat(count) * Metrics.rowHeight + CGFloat(count - 1) * Metrics.rowGap
        return max(0, min(rows, room))
    }

    let scrolls: Bool

    var body: some View {
        if scrolls {
            ScrollView(.vertical) { rows }
        } else {
            rows
        }
    }

    private var rows: some View {
        VStack(spacing: Metrics.rowGap) {
            ForEach(model.displays, id: \.key) { display in
                row(display)
            }
        }
    }

    private func row(_ display: Display) -> some View {
        let selected = display.key == model.selected?.key
        let hovered = hover == display.key
        return HStack(spacing: 12) {
            Image(systemName: "display")
                .symbolRenderingMode(.monochrome)
                .font(.system(size: 18))
                .foregroundStyle(Color.text)
                .frame(width: 24)
            VStack(alignment: .leading, spacing: 1) {
                Text(display.name)
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundStyle(Color.text)
                Text(SettingsModel.detail(display))
                    .font(.system(size: 11))
                    .foregroundStyle(Color.textDim)
            }
            .lineLimit(1)
            Spacer(minLength: 0)
        }
        .padding(.leading, 14)
        .padding(.trailing, 8)
        .frame(width: Metrics.leftWidth, height: Metrics.rowHeight)
        .background(
            RoundedRectangle(cornerRadius: 6)
                .fill(hovered ? Color.hover : Color.clear)
                .animation(Motion.hover, value: hovered)
        )
        .background(
            RoundedRectangle(cornerRadius: 6)
                .fill(selected ? Color.selectedRow : Color.clear)
                .animation(Motion.select, value: selected)
        )
        .overlay(alignment: .leading) {
            RoundedRectangle(cornerRadius: 1.5)
                .fill(Color.accent)
                .frame(width: 3, height: 16)
                .opacity(selected ? 1 : 0)
                .animation(Motion.select, value: selected)
        }
        .contentShape(Rectangle())
        .onHover { inside in
            if inside {
                hover = display.key
            } else if hover == display.key {
                hover = nil
            }
        }
        .onTapGesture { model.select(display) }
    }
}
