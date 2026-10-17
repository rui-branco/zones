import AppKit
import SwiftUI
import ZonesCore

/// The ready-made layouts as one row of tiles, each a miniature of the
/// display carved up the way that layout would carve it, its name under it.
/// Picking one is the quick route; the zone editor below is for anything the
/// presets do not cover.
struct LayoutTiles: View {
    @ObservedObject var model: SettingsModel
    /// The display's width over its height: the shape of every miniature.
    let aspect: CGFloat
    /// The column's width: the tiles share it, up to their widest.
    let width: CGFloat
    @State private var hover: Int?

    var body: some View {
        let selected = model.editing.presetIndex()
        let count = CGFloat(Presets.all.count)
        let tileWidth = min(Metrics.tileMaxWidth, ((width - 8 * (count - 1)) / count).rounded(.down))
        return HStack(spacing: 8) {
            ForEach(0..<Presets.all.count, id: \.self) { i in
                tile(i, selected: i == selected)
                    .frame(width: tileWidth, height: Metrics.tileHeight)
            }
        }
    }

    private func tile(_ i: Int, selected: Bool) -> some View {
        let hovered = !selected && hover == i
        return Button {
            model.pickPreset(i)
        } label: {
            VStack(spacing: 0) {
                MiniLayout(layout: Presets.all[i], aspect: aspect, spacing: 3,
                           color: selected ? Color.accent : Color.textFaint)
                    .padding(.horizontal, 12)
                    .padding(.top, 12)
                Text(ZoneLayout.presetNames[i])
                    .font(.system(size: 11))
                    .foregroundStyle(selected ? Color.text : Color.textDim)
                    .lineLimit(1)
                    .frame(height: 28)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(
                // The chosen one keeps the resting fill and takes an accent
                // edge instead, so hover is only ever on the others.
                RoundedRectangle(cornerRadius: 6)
                    .fill(hovered ? Color.hover : Color.clear)
                    .animation(Motion.hover, value: hovered)
                    .background(RoundedRectangle(cornerRadius: 6).fill(Color.cardBack))
            )
            .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(Color.hairline, lineWidth: 1))
            .overlay(
                RoundedRectangle(cornerRadius: 6)
                    .strokeBorder(Color.accent, lineWidth: 2)
                    .opacity(selected ? 1 : 0)
                    .animation(Motion.select, value: selected)
            )
            .contentShape(RoundedRectangle(cornerRadius: 6))
        }
        .buttonStyle(.plain)
        .onHover { inside in
            if inside {
                hover = i
            } else if hover == i {
                hover = nil
            }
        }
    }
}

/// A display's sizes as a row of chips beside the "Zones" heading, each a
/// miniature of its own layout, the active one edged in the accent. A click
/// flips the display to that size, windows and all; "+" adds a copy of the
/// active one to tweak; a right-click takes one away.
struct SizeStrip: View {
    @ObservedObject var model: SettingsModel
    let layouts: DisplayLayouts
    let aspect: CGFloat

    /// The chip under the pointer; -1 for "+".
    @State private var hover: Int?

    var body: some View {
        HStack(spacing: 6) {
            Text("Sizes")
                .font(.system(size: 11))
                .foregroundStyle(Color.textDim)
                .padding(.trailing, 2)
            ForEach(layouts.sizes.indices, id: \.self) { i in
                chip(i)
            }
            addChip
        }
    }

    private func chip(_ i: Int) -> some View {
        let active = i == layouts.active
        return Button {
            model.activateSize(i)
        } label: {
            MiniLayout(layout: layouts.sizes[i], aspect: aspect, spacing: 2,
                       color: active ? Color.accent : Color.textFaint)
                .padding(.horizontal, 6)
                .padding(.vertical, 5)
                .frame(width: 52, height: 32)
                .background(plate(hovered: !active && hover == i))
                .overlay(RoundedRectangle(cornerRadius: 4).strokeBorder(Color.hairline, lineWidth: 1))
                .overlay(
                    RoundedRectangle(cornerRadius: 4)
                        .strokeBorder(Color.accent, lineWidth: 1.5)
                        .opacity(active ? 1 : 0)
                        .animation(Motion.select, value: active)
                )
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { track($0, i) }
        .contextMenu {
            Button("Remove Size") { model.removeSize(i) }
                .disabled(layouts.sizes.count < 2)
        }
    }

    private var addChip: some View {
        let full = layouts.sizes.count >= SettingsModel.maxSizes
        return Button {
            model.addSize()
        } label: {
            Image(systemName: "plus")
                .font(.system(size: 12, weight: .medium))
                .foregroundStyle(full ? Color.textFaint : Color.textDim)
                .frame(width: 32, height: 32)
                .background(plate(hovered: !full && hover == -1))
                .overlay(RoundedRectangle(cornerRadius: 4).strokeBorder(Color.hairline, lineWidth: 1))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(full)
        .onHover { track($0, -1) }
    }

    private func plate(hovered: Bool) -> some View {
        RoundedRectangle(cornerRadius: 4)
            .fill(hovered ? Color.hover : Color.clear)
            .animation(Motion.hover, value: hovered)
            .background(RoundedRectangle(cornerRadius: 4).fill(Color.cardBack))
    }

    private func track(_ inside: Bool, _ i: Int) {
        if inside {
            hover = i
        } else if hover == i {
            hover = nil
        }
    }
}
