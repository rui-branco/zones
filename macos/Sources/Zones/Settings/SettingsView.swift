import AppKit
import SwiftUI
import ZonesCore

/// The settings page: the displays and the snapping options down the left,
/// and on the right the layout of whichever display is picked, chosen from
/// the presets or drawn by hand, and its sizes.
///
/// Placed point by point rather than flowed, mirroring the Windows window,
/// which it is meant to be recognisably the same page as. The left column
/// keeps its width; the right one, and the zone editor in it, take whatever
/// the window gives them. The header is flat with the page, in the window's
/// own colour: the traffic lights sit on it and the window is dragged by it,
/// with no title bar stacked on top. Its right end has the version, the
/// updates and starting at login.
struct SettingsView: View {
    @ObservedObject var model: SettingsModel

    private static let hint = "Click to split  ·  Drag a line to resize  ·  Right-click for more"

    /// This copy's version; "dev" for a build run outside its bundle, which has none.
    private static let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev"

    var body: some View {
        // The whole window, the strip under the traffic lights included:
        // the page draws its own header there.
        GeometryReader { geometry in
            page(geometry.size)
        }
        .ignoresSafeArea()
    }

    private func page(_ size: CGSize) -> some View {
        ZStack(alignment: .topLeading) {
            Color.windowBack
            header(width: size.width)
            leftColumn(height: size.height)
            if let display = model.selected {
                rightColumn(display, size: size)
            }
        }
        .frame(width: size.width, height: size.height, alignment: .topLeading)
    }

    // MARK: - Header

    /// The icon and the name beside the traffic lights; at the other end
    /// the version, the update button and the auto-update and start at
    /// login switches.
    private func header(width: CGFloat) -> some View {
        Group {
            HStack(spacing: 6) {
                Image(nsImage: NSApp?.applicationIconImage ?? NSImage())
                    .resizable()
                    .interpolation(.high)
                    .frame(width: 16, height: 16)
                Text("Zones")
                    .font(.system(size: 13))
                    .foregroundStyle(Color.text)
            }
            .place(Metrics.headerX, 0, height: Metrics.headerHeight, alignment: .leading)

            HStack(spacing: 16) {
                HStack(spacing: 12) {
                    Text("Version \(SettingsView.version)")
                        .font(.system(size: 11))
                        .foregroundStyle(Color.textDim)
                    updateButton
                }
                HStack(spacing: 6) {
                    Text("Auto-update")
                        .font(.system(size: 11))
                        .foregroundStyle(Color.text)
                    Toggle("Auto-update", isOn: Binding(get: { model.config.autoUpdate },
                                                        set: { model.setAutoUpdate($0) }))
                        .labelsHidden()
                        .toggleStyle(.switch)
                        .controlSize(.small)
                }
                HStack(spacing: 6) {
                    Text("Start at Login")
                        .font(.system(size: 11))
                        .foregroundStyle(Color.text)
                    Toggle("Start at Login", isOn: Binding(get: { model.startsAtLogin },
                                                           set: { model.setStartAtLogin($0) }))
                        .labelsHidden()
                        .toggleStyle(.switch)
                        .controlSize(.small)
                }
            }
            .lineLimit(1)
            .fixedSize()
            .place(0, 0, width: width - Metrics.headerTrailing, height: Metrics.headerHeight, alignment: .trailing)
        }
    }

    // MARK: - Left column

    private func leftColumn(height: CGFloat) -> some View {
        let footnote = SnappingCard.footnote(model.takenSlots)
        let footnoteRoom: CGFloat = footnote == nil ? 0 : 22

        // The list gets what the card under it leaves, down to the bottom margin.
        let room = height - Metrics.margin - footnoteRoom - SnappingCard.height - Metrics.headingGap
            - Metrics.sectionGap - Metrics.listTop
        let listHeight = DisplayList.height(for: model.displays.count, room: room)
        let full = DisplayList.height(for: model.displays.count, room: .infinity)
        let snappingY = Metrics.listTop + listHeight + Metrics.sectionGap
        let cardY = snappingY + Metrics.headingGap

        return Group {
            Heading("Displays")
                .place(Metrics.leftX, 56)
            DisplayList(model: model, scrolls: listHeight < full)
                .place(Metrics.leftX, Metrics.listTop, width: Metrics.leftWidth, height: listHeight)
            Heading("Snapping")
                .place(Metrics.leftX, snappingY)
            SnappingCard(model: model)
                .place(Metrics.leftX, cardY, width: Metrics.leftWidth, height: SnappingCard.height)
            if let footnote {
                Text(footnote)
                    .font(.system(size: 11))
                    .foregroundStyle(Color.danger)
                    .lineLimit(1)
                    .place(Metrics.leftX, cardY + SnappingCard.height + 8, width: Metrics.leftWidth)
            }
        }
    }

    // MARK: - Right column

    @ViewBuilder
    private func rightColumn(_ display: Display, size: CGSize) -> some View {
        let layouts = model.layouts(of: display)
        let visible = display.visibleFrame.size
        let aspect = visible.height > 0 ? visible.width / visible.height : 16.0 / 9.0
        let x = Metrics.rightX
        let width = size.width - x - Metrics.margin
        let cardBottom = size.height - Metrics.margin

        titleRow(display)
            .place(x, 50, width: width)
        subtitle(display)
            .place(x, 84, width: width)

        Heading("Layouts")
            .place(x, 128)
        LayoutTiles(model: model, aspect: aspect, width: width)
            .place(x, 156, width: width, height: Metrics.tileHeight)

        Heading("Zones")
            .place(x, 264)
        SizeStrip(model: model, layouts: layouts, aspect: aspect)
            .place(x, 256, width: width, height: 32, alignment: .trailing)

        Group {
            Card()
                .place(x, Metrics.editorTop, width: width, height: cardBottom - Metrics.editorTop)
            // 12 in from the card's sides and top, ending 26 above its
            // bottom for the hint.
            ZoneEditor(layout: model.editing, displaySize: visible, spacing: CGFloat(model.config.spacing),
                       revision: model.revision, onCommit: { model.commitEditing() })
                .place(x + 12, Metrics.editorTop + 12, width: width - 24,
                       height: cardBottom - 26 - Metrics.editorTop - 12)
            Text(SettingsView.hint)
                .font(.system(size: 11))
                .foregroundStyle(Color.textDim)
                .lineLimit(1)
                .place(x, cardBottom - 26, width: width, height: 26, alignment: .center)
        }
    }

    /// The display's name, and level with it the page's actions: the one
    /// used most as a button, the rest in a menu.
    private func titleRow(_ display: Display) -> some View {
        HStack(alignment: .top, spacing: 16) {
            Text(display.name)
                .font(.system(size: 24, weight: .semibold))
                .foregroundStyle(Color.text)
                .lineLimit(1)
            Spacer(minLength: 0)
            HStack(spacing: 8) {
                Button {
                    model.showOnScreen()
                } label: {
                    Label("Show on Screen", systemImage: "rectangle.on.rectangle")
                }
                .controlSize(.regular)
                Menu {
                    // Only means something with more than one display.
                    if model.displays.count > 1 {
                        Button("Use on All Displays") { model.useOnAllDisplays() }
                    }
                    Button("Reset This Display") { model.reset() }
                } label: {
                    Image(systemName: "ellipsis.circle")
                        .font(.system(size: 15))
                }
                .menuStyle(.button)
                .buttonStyle(.borderless)
                .menuIndicator(.hidden)
                .help("More")
            }
            .fixedSize()
            .padding(.top, 4)
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

    /// "1800 × 1169  ·  Main"; for a moment after "Use on All Displays",
    /// word that it worked, as nothing else on screen changes to show it.
    private func subtitle(_ display: Display) -> some View {
        ZStack(alignment: .topLeading) {
            if model.copied {
                Text("Copied to every display")
                    .foregroundStyle(Color.accent)
                    .transition(.opacity)
            } else {
                Text(SettingsModel.detail(display))
                    .foregroundStyle(Color.textDim)
                    .transition(.opacity)
            }
        }
        .font(.system(size: 12))
        .lineLimit(1)
        .animation(Motion.select, value: model.copied)
    }
}
