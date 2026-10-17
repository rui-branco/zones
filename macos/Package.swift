// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "Zones",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "Zones", targets: ["Zones"]),
        .library(name: "ZonesCore", targets: ["ZonesCore"]),
    ],
    targets: [
        // Foundation and CoreGraphics only, so the layout model and config
        // can be tested headless, without a window server.
        .target(name: "ZonesCore"),
        .executableTarget(name: "Zones", dependencies: ["ZonesCore"]),
        .testTarget(name: "ZonesCoreTests", dependencies: ["ZonesCore"]),
    ],
    swiftLanguageVersions: [.v5]
)
