// swift-tools-version:6.0
import PackageDescription

var targets: [Target] = [
    .target(name: "HueCore"),
    .target(name: "HueKit", dependencies: ["HueCore"], swiftSettings: [.swiftLanguageMode(.v5)]),
    .executableTarget(name: "ColorLab", dependencies: ["HueCore"]),
    .testTarget(name: "HueCoreTests", dependencies: ["HueCore"]),
]
var products: [Product] = []

#if os(macOS)
targets += [
    .target(name: "CaptureKit", dependencies: ["HueCore"], swiftSettings: [.swiftLanguageMode(.v5)]),
    .executableTarget(name: "Lumivane", dependencies: ["HueCore", "HueKit", "CaptureKit"],
                      swiftSettings: [.swiftLanguageMode(.v5)]),
]
products += [.executable(name: "Lumivane", targets: ["Lumivane"])]
#endif

let package = Package(
    name: "Lumivane",
    platforms: [.macOS(.v14)],
    products: products,
    targets: targets
)
