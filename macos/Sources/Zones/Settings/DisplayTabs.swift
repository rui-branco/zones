import AppKit
import SwiftUI

/// The displays as a row of tabs across the top of the Layouts page, one
/// each, the chosen one marked with an accent pill at its bottom edge that
/// slides to the next one picked.
///
/// Each tab gives the display's name and its size and whether it is the main
/// one: what tells two of the same make apart in System Settings as well. As
/// wide as its words; with more displays than fit, the tabs narrow and their
/// words are cut short.
struct DisplayTabs: View {
    @ObservedObject var model: SettingsModel
    @State private var hover: String?
    @Namespace private var pill

    var body: some View {
        HStack(spacing: Metrics.tabGap) {
            ForEach(model.displays, id: \.key) { display in
                tab(display)
            }
        }
        .animation(Motion.select, value: model.selected?.key)
    }

    private func tab(_ display: Display) -> some View {
        let selected = display.key == model.selected?.key
        let hovered = hover == display.key
        return HStack(spacing: 10) {
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
            .truncationMode(.tail)
        }
        .padding(.leading, 12)
        .padding(.trailing, 14)
        .frame(height: Metrics.tabHeight)
        .background(
            RoundedRectangle(cornerRadius: 6)
                .fill(selected || hovered ? Color.hover : Color.clear)
                .animation(Motion.hover, value: hovered)
        )
        .overlay(alignment: .bottom) {
            if selected {
                RoundedRectangle(cornerRadius: 1.5)
                    .fill(Color.accent)
                    .frame(width: 16, height: 3)
                    .matchedGeometryEffect(id: "pill", in: pill)
            }
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
