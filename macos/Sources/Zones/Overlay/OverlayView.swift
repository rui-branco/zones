import AppKit

/// What the overlay window draws: every zone as a frosted plate with its
/// number, the ones the window would land in filled with the accent, and an
/// optional label as a pill at the top of the usable area.
///
/// Flipped, so it works in the same y-down points as everything else, from
/// the display's top-left corner. Drawn only when what it shows changes: a
/// drag hands it the same picture many times a second, and redrawing a whole
/// display for nothing is the expensive part.
final class OverlayView: NSView {
    struct Content: Equatable {
        /// In the view's own points.
        var zones: [CGRect] = []
        var selected: Set<Int> = []
        var label: String?
        /// Where the label's top edge goes, and the x it centres on.
        var labelTop: CGFloat = 0
        var labelCentre: CGFloat = 0
    }

    var content = Content() {
        didSet {
            if content != oldValue { needsDisplay = true }
        }
    }

    override var isFlipped: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        // See-through everywhere nothing is drawn.
        NSColor.clear.set()
        dirtyRect.fill(using: .copy)

        for (i, zone) in content.zones.enumerated() {
            drawZone(zone, number: i + 1, lit: content.selected.contains(i))
        }
        if let label = content.label {
            drawLabel(label)
        }
    }

    private func drawZone(_ zone: CGRect, number: Int, lit: Bool) {
        let r = zone.insetBy(dx: 3, dy: 3)
        guard r.width >= 4, r.height >= 4 else { return }

        let plate = NSBezierPath(roundedRect: r, xRadius: 10, yRadius: 10)
        if lit {
            NSColor.controlAccentColor.withAlphaComponent(0.55).setFill()
            NSColor.controlAccentColor.setStroke()
            plate.lineWidth = 2
        } else {
            NSColor.windowBackgroundColor.withAlphaComponent(0.55).setFill()
            NSColor.labelColor.withAlphaComponent(0.35).setStroke()
            plate.lineWidth = 1.5
        }
        plate.fill()
        plate.stroke()

        // The number, as big as the zone comfortably takes, and the zone's
        // size under it when there is room for a second line.
        let px = min(40, 0.4 * min(r.width, r.height))
        guard px >= 8 else { return }

        let digits = NSAttributedString(string: "\(number)", attributes: [
            .font: NSFont.systemFont(ofSize: px, weight: .semibold),
            .foregroundColor: NSColor.labelColor,
        ])
        let size = r.width > 120 && r.height > 120
            ? NSAttributedString(string: "\(Int(zone.width.rounded())) × \(Int(zone.height.rounded()))", attributes: [
                .font: NSFont.systemFont(ofSize: 13),
                .foregroundColor: NSColor.secondaryLabelColor,
            ])
            : nil

        let digitsSize = digits.size()
        let sizeSize = size?.size() ?? .zero
        let top = r.midY - (digitsSize.height + sizeSize.height) / 2
        digits.draw(at: CGPoint(x: r.midX - digitsSize.width / 2, y: top))
        size?.draw(at: CGPoint(x: r.midX - sizeSize.width / 2, y: top + digitsSize.height))
    }

    private func drawLabel(_ text: String) {
        let string = NSAttributedString(string: text, attributes: [
            .font: NSFont.systemFont(ofSize: 14, weight: .semibold),
            .foregroundColor: NSColor.labelColor,
        ])
        let textSize = string.size()
        let width = (textSize.width + 28).rounded(.up)
        let height = (textSize.height + 14).rounded(.up)
        let pill = CGRect(x: (content.labelCentre - width / 2).rounded(), y: content.labelTop.rounded(),
                          width: width, height: height)

        // The 1pt edge sits half a point in, so it lands on whole pixels.
        let path = NSBezierPath(roundedRect: pill.insetBy(dx: 0.5, dy: 0.5),
                                xRadius: height / 2 - 0.5, yRadius: height / 2 - 0.5)
        NSColor.windowBackgroundColor.withAlphaComponent(0.92).setFill()
        path.fill()
        NSColor.separatorColor.setStroke()
        path.lineWidth = 1
        path.stroke()

        string.draw(at: CGPoint(x: pill.minX + (width - textSize.width) / 2, y: pill.minY + (height - textSize.height) / 2))
    }
}
