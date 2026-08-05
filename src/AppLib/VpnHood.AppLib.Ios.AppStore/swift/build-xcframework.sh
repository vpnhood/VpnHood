#!/usr/bin/env bash
#
# build-xcframework.sh — build VpnHoodStoreKit.xcframework from the Swift
# facade. Run ON A MAC with Xcode 15+:
#
#   cd src/AppLib/VpnHood.AppLib.Ios.AppStore/swift && ./build-xcframework.sh
#
# Output lands at swift/VpnHoodStoreKit.xcframework, where the csproj's
# conditional NativeReference picks it up. Commit the built xcframework (it is
# small and changes only when StoreKitBridge.swift does) so Windows/CI builds
# of the iOS app never need a Swift toolchain.

set -euo pipefail

cd "$(dirname "$0")"
DERIVED="$PWD/.build-xcframework"
rm -rf "$DERIVED" VpnHoodStoreKit.xcframework

build() {
  local sdk="$1" dest="$2"
  xcodebuild archive \
    -workspace VpnHoodStoreKit \
    -scheme VpnHoodStoreKit \
    -destination "$dest" \
    -sdk "$sdk" \
    -archivePath "$DERIVED/$sdk" \
    SKIP_INSTALL=NO \
    BUILD_LIBRARY_FOR_DISTRIBUTION=YES \
    OTHER_SWIFT_FLAGS='-enable-library-evolution'
}

build iphoneos        'generic/platform=iOS'
build iphonesimulator 'generic/platform=iOS Simulator'

xcodebuild -create-xcframework \
  -archive "$DERIVED/iphoneos.xcarchive"        -framework VpnHoodStoreKit.framework \
  -archive "$DERIVED/iphonesimulator.xcarchive" -framework VpnHoodStoreKit.framework \
  -output VpnHoodStoreKit.xcframework

rm -rf "$DERIVED"
echo "Built VpnHoodStoreKit.xcframework"
