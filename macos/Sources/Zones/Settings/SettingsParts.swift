import AppKit
import SwiftUI
import ZonesCore

/// Where everything in the settings window sits, in points from the top-left
/// corner of its content. Placed rather than flowed, as the Windows version's
/// parts are, so the two windows stay recognisably the same page; only the
/// page beside the sidebar and the editor's card stretch with the window.
enum Metrics {
    /// The window's content size when it opens, and the snapshot's.
    static let size = CGSize(width: 1200, height: 680)
    /// Below this the tiles and the zone editor get too cramped to use. The
    /// other pages, a card or two each, fit in less.
    static let minSize = CGSize(width: 900, height: 600)

    /// The flat band at the top the traffic lights, the icon and the name share.
    static let headerHeight: CGFloat = 40
    /// Where the icon and the name start: clear of the traffic lights.
    static let headerX: CGFloat = 80

    /// The sidebar down the window's left side, its full height.
    static let sidebarWidth: CGFloat = 220
    /// The first section in the sidebar: level with the page's title, or
    /// the display tabs, beside it.
    static let navTop: CGFloat = 48
    /// A section in the sidebar, the gap to the next, and the inset from the
    /// sidebar's sides.
    static let navItemHeight: CGFloat = 32
    static let navGap: CGFloat = 4
    static let navInset: CGFloat = 10
    /// From the version at the sidebar's foot to its left and bottom edges.
    static let footerMargin: CGFloat = 16

    /// The margin round the page: clear of the sidebar, and to the window's
    /// right and bottom edges.
    static let margin: CGFloat = 24
    /// The page's left edge.
    static let pageX: CGFloat = sidebarWidth + margin

    /// A page's title.
    static let titleY: CGFloat = 50
    /// The Layouts page has the display tabs where the title would be: a
    /// tab's height, and the gap between two.
    static let tabsY: CGFloat = 48
    static let tabHeight: CGFloat = 52
    static let tabGap: CGFloat = 8

    /// The first card under a page's title, and the gap between two.
    static let cardsTop: CGFloat = 96
    static let cardGap: CGFloat = 16
    /// A row of a card: a title and a line under it.
    static let rowHeight: CGFloat = 52
    /// The widest a card grows: past that a row's words and its control
    /// are too far apart to read as one.
    static let cardMaxWidth: CGFloat = 860

    /// A preset tile's height, and the most one grows across a wide window:
    /// past that the miniatures only get emptier.
    static let tileHeight: CGFloat = 84
    static let tileMaxWidth: CGFloat = 120

    /// The top of the zone editor's card.
    static let editorTop: CGFloat = 300
}

/// The window's animations, all of them short: they show what changed, and
/// none when the user has asked macOS to reduce motion.
@MainActor
enum Motion {
    static var reduced: Bool {
        NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
    }

    /// An ease-out of `duration` seconds, or none with reduced motion.
    static func ease(_ duration: Double) -> Animation? {
        reduced ? nil : .easeOut(duration: duration)
    }

    /// A selection moving: the sidebar's or the display tabs' pill, a tile's
    /// or chip's edge.
    static var select: Animation? {
        ease(0.17)
    }

    /// A hover fill coming and going: quick, so it never trails the pointer.
    static var hover: Animation? {
        ease(0.08)
    }
}

/// The few colours the window works out rather than takes as they are, each
/// built on a system one so it follows light and dark with the rest.
enum Palette {
    /// Under whatever the pointer is over: a breath darker in light, lighter in dark.
    static let hover = NSColor(name: nil) { appearance in
        isDark(appearance) ? NSColor.white.withAlphaComponent(0.06) : NSColor.black.withAlphaComponent(0.04)
    }

    /// The model display in the zone editor: a shade under the card, sunk
    /// into it, so the zones on it, in the window's own colour, stand up off it.
    static let plate = NSColor(name: nil) { appearance in
        NSColor.black.withAlphaComponent(isDark(appearance) ? 0.3 : 0.06)
    }

    /// A hovered zone: the accent, faint enough for the number to stay legible on it.
    static let accentTint = NSColor(name: nil) { appearance in
        NSColor.controlAccentColor.withAlphaComponent(isDark(appearance) ? 0.16 : 0.11)
    }

    static func isDark(_ appearance: NSAppearance) -> Bool {
        appearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
    }
}

extension Color {
    static let windowBack = Color(nsColor: .windowBackgroundColor)
    static let cardBack = Color(nsColor: .controlBackgroundColor)
    static let hairline = Color(nsColor: .separatorColor)
    static let accent = Color(nsColor: .controlAccentColor)
    static let text = Color(nsColor: .labelColor)
    static let textDim = Color(nsColor: .secondaryLabelColor)
    static let textFaint = Color(nsColor: .tertiaryLabelColor)
    static let hover = Color(nsColor: Palette.hover)
    static let danger = Color(nsColor: .systemRed)
}

extension View {
    /// Puts the view's top-left corner at (`x`, `y`) in the window, sized
    /// `width` × `height` where given. Inside the window's top-leading ZStack.
    func place(_ x: CGFloat, _ y: CGFloat, width: CGFloat? = nil, height: CGFloat? = nil,
               alignment: Alignment = .topLeading) -> some View {
        frame(width: width, height: height, alignment: alignment).offset(x: x, y: y)
    }
}

/// A section's name over it, on the Layouts page: "Presets", "Zones".
struct Heading: View {
    let title: String

    init(_ title: String) {
        self.title = title
    }

    var body: some View {
        Text(title)
            .font(.system(size: 13, weight: .semibold))
            .foregroundStyle(Color.text)
    }
}

/// A plate raised off the window for a group of settings: rounded, filled,
/// with a hairline edge.
struct Card: View {
    var body: some View {
        RoundedRectangle(cornerRadius: 8)
            .fill(Color.cardBack)
            .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Color.hairline, lineWidth: 1))
    }
}

/// A layout in miniature: its zones in the display's own proportions, as
/// large as fit and centred, so "Thirds" on an ultrawide looks like what it
/// will be there. Just the zones: their gaps already draw the outline.
struct MiniLayout: View {
    let layout: ZoneLayout
    /// The display's width over its height.
    let aspect: CGFloat
    let spacing: CGFloat
    let color: Color

    var body: some View {
        Canvas { context, size in
            guard aspect > 0, size.width > 0, size.height > 0 else { return }
            let w = min(size.width, size.height * aspect)
            let h = w / aspect
            let box = CGRect(x: (size.width - w) / 2, y: (size.height - h) / 2, width: w, height: h)
            for zone in layout.zonesIn(box, spacing: spacing) {
                context.fill(Path(roundedRect: zone, cornerRadius: 2), with: .color(color))
            }
        }
    }
}

/// The ready-made layouts, built once: they never change, and every tile
/// draws one on every update.
@MainActor
enum Presets {
    static let all: [ZoneLayout] = (0..<ZoneLayout.presetNames.count).map { ZoneLayout.preset($0) }
}
