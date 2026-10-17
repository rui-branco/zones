// Draws the Zones app icon and packs it into an .icns.
//
//     swift scripts/make-icon.swift <out.icns>
//
// The mark is the Windows version's (windows/src/Ui/Logo.cs), the same
// geometry and colours, so the two apps carry the same icon: a screen split
// into three zones, 1 : 1.7 : 1, the middle one lit. Here it sits on the
// rounded-square plate macOS icons share, at the plate size and corner of
// Apple's icon grid. Every size is drawn on its own rather than scaled down
// from the biggest, so the small ones stay sharp.
//
// The PNGs are left next to the .icns, in <out>.iconset, for a look at them.

import CoreGraphics
import Foundation
import ImageIO

let args = CommandLine.arguments
guard args.count == 2 else {
    FileHandle.standardError.write(Data("usage: swift make-icon.swift <out.icns>\n".utf8))
    exit(2)
}

let out = URL(fileURLWithPath: args[1])
let iconset = out.deletingPathExtension().appendingPathExtension("iconset")

func color(_ hex: UInt32, _ alpha: CGFloat = 1) -> CGColor {
    CGColor(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255,
            blue: CGFloat(hex & 0xFF) / 255, alpha: alpha)
}

let plateTop = color(0x2E2A6B)
let plateBottom = color(0x17153A)
let screenTop = color(0x8F80FF)
let screenBottom = color(0x5140E8)
let litTop = color(0x6BE4FF)
let litBottom = color(0x2186FF)
let zone = color(0xFFFFFF, 150.0 / 255)
let shine = color(0xFFFFFF, 120.0 / 255)
let rim = color(0xFFFFFF, 0.12)

func rounded(_ r: CGRect, _ radius: CGFloat) -> CGPath {
    let radius = max(0, min(radius, r.width / 2, r.height / 2))
    return CGPath(roundedRect: r, cornerWidth: radius, cornerHeight: radius, transform: nil)
}

/// Fills `path` with a top-to-bottom gradient spanning `r`. Extended past
/// both ends, so an anti-aliased edge just outside the box still gets the
/// colour of the end it is nearest.
func fillVertical(_ ctx: CGContext, _ path: CGPath, _ r: CGRect, _ top: CGColor, _ bottom: CGColor) {
    guard let gradient = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB),
                                    colors: [top, bottom] as CFArray, locations: [0, 1]) else { return }
    ctx.saveGState()
    ctx.addPath(path)
    ctx.clip()
    ctx.drawLinearGradient(gradient, start: CGPoint(x: r.minX, y: r.minY), end: CGPoint(x: r.minX, y: r.maxY),
                           options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
    ctx.restoreGState()
}

/// One icon, `px` pixels square, drawn y-down like Logo.cs.
func draw(_ ctx: CGContext, _ px: CGFloat) {
    ctx.translateBy(x: 0, y: px)
    ctx.scaleBy(x: 1, y: -1)

    // The plate: 824 of 1024 with a 22.5% corner, as Apple's grid has it.
    let inset = px * 100 / 1024
    let plate = CGRect(x: inset, y: inset, width: px - 2 * inset, height: px - 2 * inset)
    let corner = plate.width * 0.225
    fillVertical(ctx, rounded(plate, corner), plate, plateTop, plateBottom)

    // A faint line just inside its edge, where light would catch it.
    ctx.addPath(rounded(plate.insetBy(dx: 0.5, dy: 0.5), corner - 0.5))
    ctx.setStrokeColor(rim)
    ctx.setLineWidth(1)
    ctx.strokePath()

    // The mark, centred on the plate at 78% of it. From here on this is
    // Logo.Draw, fraction for fraction, in a box of side `s`.
    let s = plate.width * 0.78
    let x = plate.midX - s / 2
    let y = plate.midY - s / 2

    // 1. The screen: wider than tall, like the displays the zones are laid out on.
    let screen = CGRect(x: x + 0.04 * s, y: y + 0.16 * s, width: 0.92 * s, height: 0.68 * s)
    fillVertical(ctx, rounded(screen, 0.12 * s), screen, screenTop, screenBottom)

    // 2. Three zones side by side, the middle one wider, as the Focus layout
    // has them. The screen shows through the gaps between them.
    let inside = screen.insetBy(dx: 0.08 * s, dy: 0.08 * s)
    let gap = 0.05 * s
    let unit = (inside.width - 2 * gap) / 3.7
    let left = CGRect(x: inside.minX, y: inside.minY, width: unit, height: inside.height)
    let middle = CGRect(x: left.maxX + gap, y: inside.minY, width: 1.7 * unit, height: inside.height)
    let right = CGRect(x: middle.maxX + gap, y: inside.minY, width: inside.maxX - (middle.maxX + gap), height: inside.height)

    ctx.setFillColor(zone)
    ctx.addPath(rounded(left, 0.05 * s))
    ctx.addPath(rounded(right, 0.05 * s))
    ctx.fillPath()

    // 3. The middle zone lit, in the colours a window takes on as it is
    // dropped into one.
    fillVertical(ctx, rounded(middle, 0.05 * s), middle, litTop, litBottom)

    // Its top edge catches the light. Below 20px a line that thin only
    // muddies the edge.
    if s >= 20 {
        ctx.setStrokeColor(shine)
        ctx.setLineWidth(1)
        ctx.move(to: CGPoint(x: middle.minX + 0.05 * s, y: middle.minY + 0.5))
        ctx.addLine(to: CGPoint(x: middle.maxX - 0.05 * s, y: middle.minY + 0.5))
        ctx.strokePath()
    }
}

func render(_ px: Int, to url: URL) -> Bool {
    guard let space = CGColorSpace(name: CGColorSpace.sRGB),
          let ctx = CGContext(data: nil, width: px, height: px, bitsPerComponent: 8, bytesPerRow: 0, space: space,
                              bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return false }
    ctx.setShouldAntialias(true)
    draw(ctx, CGFloat(px))

    guard let image = ctx.makeImage(),
          let destination = CGImageDestinationCreateWithURL(url as CFURL, "public.png" as CFString, 1, nil)
    else { return false }
    CGImageDestinationAddImage(destination, image, nil)
    return CGImageDestinationFinalize(destination)
}

// What iconutil expects to find in an .iconset: each size at 1x and 2x.
let renditions: [(name: String, px: Int)] = [
    ("icon_16x16", 16), ("icon_16x16@2x", 32),
    ("icon_32x32", 32), ("icon_32x32@2x", 64),
    ("icon_128x128", 128), ("icon_128x128@2x", 256),
    ("icon_256x256", 256), ("icon_256x256@2x", 512),
    ("icon_512x512", 512), ("icon_512x512@2x", 1024),
]

do {
    try? FileManager.default.removeItem(at: iconset)
    try FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)
} catch {
    FileHandle.standardError.write(Data("make-icon: cannot create \(iconset.path): \(error)\n".utf8))
    exit(1)
}

for rendition in renditions {
    let url = iconset.appendingPathComponent(rendition.name + ".png")
    guard render(rendition.px, to: url) else {
        FileHandle.standardError.write(Data("make-icon: cannot write \(url.path)\n".utf8))
        exit(1)
    }
}

let iconutil = Process()
iconutil.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
iconutil.arguments = ["-c", "icns", iconset.path, "-o", out.path]
do {
    try iconutil.run()
    iconutil.waitUntilExit()
} catch {
    FileHandle.standardError.write(Data("make-icon: cannot run iconutil: \(error)\n".utf8))
    exit(1)
}
exit(iconutil.terminationStatus == 0 ? 0 : 1)
