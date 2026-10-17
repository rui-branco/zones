import AppKit
import SwiftUI

/// The window's sections, in the order the sidebar lists them.
enum SettingsPage: CaseIterable {
    case layouts
    case snapping
    case shortcuts
    case general

    /// Its name in the sidebar, and over the page where the page has a title.
    var title: String {
        switch self {
        case .layouts: return "Layouts"
        case .snapping: return "Snapping"
        case .shortcuts: return "Shortcuts"
        case .general: return "General"
        }
    }

    /// Its SF Symbol in the sidebar.
    var symbol: String {
        switch self {
        case .layouts: return "rectangle.split.3x1"
        case .snapping: return "macwindow"
        case .shortcuts: return "keyboard"
        case .general: return "gearshape"
        }
    }
}

/// The column down the window's left side, as in System Settings: the icon
/// and the name in the band with the traffic lights, the sections under them,
/// and at the foot the version, with the update over it while one is on offer.
///
/// In the window's own colour, with no edge: the sections' fills and the
/// pages' cards are what tell the two apart. The chosen section is marked
/// with an accent pill at its edge that slides to the next one picked.
struct Sidebar: View {
    @ObservedObject var model: SettingsModel
    @Binding var page: SettingsPage
    let height: CGFloat

    @State private var hover: SettingsPage?
    @Namespace private var pill

    var body: some View {
        ZStack(alignment: .topLeading) {
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

            VStack(spacing: Metrics.navGap) {
                ForEach(SettingsPage.allCases, id: \.self) { item($0) }
            }
            .animation(Motion.select, value: page)
            .place(Metrics.navInset, Metrics.navTop, width: Metrics.sidebarWidth - 2 * Metrics.navInset)

            footer
                .place(Metrics.footerMargin, 0, width: Metrics.sidebarWidth - 2 * Metrics.footerMargin,
                       height: height - Metrics.footerMargin, alignment: .bottomLeading)
        }
        .frame(width: Metrics.sidebarWidth, height: height, alignment: .topLeading)
    }

    /// A section: its symbol and its name, filled under the pointer and
    /// while chosen, the chosen one's name in semibold.
    private func item(_ item: SettingsPage) -> some View {
        let selected = item == page
        let hovered = hover == item
        return Button {
            page = item
        } label: {
            // The symbol 12 in, centred in its own width so the four line up
            // however wide each is drawn; the name 38 in.
            HStack(spacing: 0) {
                Image(systemName: item.symbol)
                    .symbolRenderingMode(.monochrome)
                    .font(.system(size: 14))
                    .foregroundStyle(Color.text)
                    .frame(width: 18)
                    .padding(.trailing, 8)
                Text(item.title)
                    .font(.system(size: 13, weight: selected ? .semibold : .regular))
                    .foregroundStyle(Color.text)
                    .lineLimit(1)
                Spacer(minLength: 0)
            }
            .padding(.leading, 12)
            .frame(height: Metrics.navItemHeight)
            .background(
                RoundedRectangle(cornerRadius: 6)
                    .fill(selected || hovered ? Color.hover : Color.clear)
                    .animation(Motion.hover, value: hovered)
            )
            .overlay(alignment: .leading) {
                if selected {
                    RoundedRectangle(cornerRadius: 1.5)
                        .fill(Color.accent)
                        .frame(width: 3, height: 16)
                        .matchedGeometryEffect(id: "pill", in: pill)
                }
            }
            .contentShape(RoundedRectangle(cornerRadius: 6))
        }
        .buttonStyle(.plain)
        .onHover { inside in
            if inside {
                hover = item
            } else if hover == item {
                hover = nil
            }
        }
    }

    /// The version, and over it, while an update is on offer, the button
    /// that installs it: the one thing in the window waiting on the user, so
    /// it shows from every page, not only from General.
    private var footer: some View {
        VStack(alignment: .leading, spacing: 8) {
            if let version = offered {
                Button {
                    model.installUpdate()
                } label: {
                    Label("Update to \(version)", systemImage: "arrow.down.circle")
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.small)
            }
            Text("Version \(SettingsView.version)")
                .font(.system(size: 11))
                .foregroundStyle(Color.textDim)
                .lineLimit(1)
        }
    }

    /// The version on offer, if one is.
    private var offered: String? {
        if case .available(let release) = model.update { return release.version }
        return nil
    }
}
