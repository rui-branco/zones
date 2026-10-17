import AppKit
import SwiftUI
import ZonesCore

/// The settings window's content: a sidebar down the left, as in System
/// Settings, and beside it the page for the section picked there. Layouts has
/// the displays as tabs and, for whichever is picked, its layout, chosen from
/// the presets or drawn by hand, and its sizes; Snapping, Shortcuts and
/// General are a card or two of options each, under the page's title.
///
/// Placed point by point rather than flowed, mirroring the Windows window,
/// which it is meant to be recognisably the same page as. The sidebar keeps
/// its width; the page, and the zone editor on it, take whatever the window
/// gives them. The top band is flat with the page, in the window's own
/// colour: the traffic lights sit on it, the icon and the name beside them at
/// the top of the sidebar, and the window is dragged by it, with no title bar
/// stacked on top. The rest of the band is left empty for that.
struct SettingsView: View {
    @ObservedObject var model: SettingsModel

    /// The page showing: Layouts each time the window opens, as the window
    /// is mostly for that.
    @State private var page = SettingsPage.layouts

    private static let hint = "Click to split  ·  Drag a line to resize  ·  Right-click for more"

    /// This copy's version; "dev" for a build run outside its bundle, which has none.
    static let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev"

    var body: some View {
        // The whole window, the strip under the traffic lights included:
        // the sidebar draws the icon and the name there.
        GeometryReader { geometry in
            window(geometry.size)
        }
        .ignoresSafeArea()
    }

    private func window(_ size: CGSize) -> some View {
        ZStack(alignment: .topLeading) {
            Color.windowBack
            Sidebar(model: model, page: $page, height: size.height)
            content(size)
        }
        .frame(width: size.width, height: size.height, alignment: .topLeading)
    }

    @ViewBuilder
    private func content(_ size: CGSize) -> some View {
        let width = size.width - Metrics.pageX - Metrics.margin
        let cardWidth = min(width, Metrics.cardMaxWidth)
        switch page {
        case .layouts:
            if let display = model.selected {
                layoutsPage(display, size: size)
            }
        case .snapping:
            title(.snapping)
            SnappingPage(model: model)
                .place(Metrics.pageX, Metrics.cardsTop, width: cardWidth)
        case .shortcuts:
            title(.shortcuts)
            ShortcutsPage(model: model, width: cardWidth)
                .place(Metrics.pageX, Metrics.cardsTop, width: cardWidth)
        case .general:
            title(.general)
            GeneralPage(model: model)
                .place(Metrics.pageX, Metrics.cardsTop, width: cardWidth)
        }
    }

    private func title(_ page: SettingsPage) -> some View {
        Text(page.title)
            .font(.system(size: 22, weight: .semibold))
            .foregroundStyle(Color.text)
            .lineLimit(1)
            .place(Metrics.pageX, Metrics.titleY)
    }

    // MARK: - Layouts

    @ViewBuilder
    private func layoutsPage(_ display: Display, size: CGSize) -> some View {
        let layouts = model.layouts(of: display)
        let visible = display.visibleFrame.size
        let aspect = visible.height > 0 ? visible.width / visible.height : 16.0 / 9.0
        let x = Metrics.pageX
        let width = size.width - x - Metrics.margin
        let cardBottom = size.height - Metrics.margin

        displayRow
            .place(x, Metrics.tabsY, width: width, height: Metrics.tabHeight)

        presetsHeading
            .place(x, 128, width: width)
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

    /// The displays as tabs, and level with them the page's actions on the
    /// one picked: the one used most as a button, the rest in a menu. The
    /// actions keep their room; the tabs narrow when they have to.
    private var displayRow: some View {
        HStack(spacing: 16) {
            DisplayTabs(model: model)
                .layoutPriority(1)
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
        }
    }

    /// "Presets"; at the other end of its line, for a moment after "Use on
    /// All Displays", word that it worked, as nothing else on screen changes
    /// to show it.
    private var presetsHeading: some View {
        HStack(alignment: .firstTextBaseline, spacing: 16) {
            Heading("Presets")
            Spacer(minLength: 0)
            if model.copied {
                Text("Copied to every display")
                    .font(.system(size: 12))
                    .foregroundStyle(Color.accent)
                    .lineLimit(1)
                    .transition(.opacity)
            }
        }
        .animation(Motion.select, value: model.copied)
    }
}
