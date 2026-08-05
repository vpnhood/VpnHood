// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "VpnHoodStoreKit",
    platforms: [.iOS(.v15)],
    products: [
        .library(name: "VpnHoodStoreKit", type: .static, targets: ["VpnHoodStoreKit"])
    ],
    targets: [
        .target(name: "VpnHoodStoreKit", path: "Sources/VpnHoodStoreKit")
    ]
)
