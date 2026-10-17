import AppKit
import SwiftUI
import ZonesCore

/// Where everything in the settings window sits, in points from the top-left
/// corner of its content. Placed rather than flowed, as the Windows version's
/// parts are, so the two windows stay recognisably the same page; only the
/// right column and the editor's card stretch with the window.
enum Metrics {
    /// The window's content size when it opens, and the snapshot's.
    static let size = CGSize(width: 1200, height: 680)
    /// Below this the tiles and the zone editor get too cramped to use.
    static let minSize = CGSize(width: 1000, height: 640)

    /// The flat band at the top the traffic lights, the icon and the name share.
    static let headerHeight: CGFloat = 40
    /// Where the icon and the name start: clear of the traffic lights.
    static let headerX: CGFloat = 80

    static let leftX: CGFloat = 24
    static let leftWidth: CGFloat = 272
    static let rightX: CGFloat = 320

    /// The margin to the window's right and bottom edges.
    static let margin: CGFloat = 24

    static let listTop: CGFloat = 84
    /// A display row.
    static let rowHeight: CGFloat = 52
    /// Between display rows: tight enough for four to fit at the default height.
    static let rowGap: CGFloat = 2

    /// From a section heading's top to the thing it heads.
    static let headingGap: CGFloat = 28
    /// From the end of one section to the next heading.
    static let sectionGap: CGFloat = 24

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

    /// A selection moving: a display row's highlight, a tile's or chip's edge.
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
    static let selectedRow = Color(nsColor: .quaternaryLabelColor)
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

/// A section's name over it: "Displays", "Snapping", "Layouts", "Zones".
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
