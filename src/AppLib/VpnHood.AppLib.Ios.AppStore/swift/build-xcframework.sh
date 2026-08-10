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

# The package product is STATIC (DllImport "__Internal" needs the symbols inside
# the app binary), and archiving a static package product emits a prelinked
# object — no .framework ever appears in the archive. Wrap that object in a
# minimal static framework so the csproj's NativeReference (Kind=Framework)
# links it into the main executable.
wrap() {
  local sdk="$1"
  local fw="$DERIVED/$sdk/VpnHoodStoreKit.framework"
  local obj
  obj="$(find "$DERIVED/$sdk.xcarchive/Products" -name 'VpnHoodStoreKit.o' -print -quit)"
  [[ -n "$obj" ]] || { echo "No prelinked VpnHoodStoreKit.o in the $sdk archive" >&2; exit 1; }
  mkdir -p "$fw"
  libtool -static -o "$fw/VpnHoodStoreKit" "$obj"
  cat > "$fw/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleExecutable</key><string>VpnHoodStoreKit</string>
	<key>CFBundleIdentifier</key><string>com.vpnhood.VpnHoodStoreKit</string>
	<key>CFBundleName</key><string>VpnHoodStoreKit</string>
	<key>CFBundlePackageType</key><string>FMWK</string>
	<key>CFBundleShortVersionString</key><string>1.0</string>
	<key>CFBundleVersion</key><string>1</string>
	<key>MinimumOSVersion</key><string>15.0</string>
</dict>
</plist>
PLIST
}

build iphoneos        'generic/platform=iOS'
build iphonesimulator 'generic/platform=iOS Simulator'
wrap  iphoneos
wrap  iphonesimulator

xcodebuild -create-xcframework \
  -framework "$DERIVED/iphoneos/VpnHoodStoreKit.framework" \
  -framework "$DERIVED/iphonesimulator/VpnHoodStoreKit.framework" \
  -output VpnHoodStoreKit.xcframework

rm -rf "$DERIVED"
echo "Built VpnHoodStoreKit.xcframework"
