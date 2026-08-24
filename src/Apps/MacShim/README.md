# MacShim — provisioning-profile installer for running iOS apps on a Mac

A stub iOS app + packet-tunnel extension whose ONLY job is to make Xcode install the real dev
provisioning profiles into macOS's SIP-protected profile store (`/var/db/MobileIdentityService`),
which AMFI checks when spawning a Network Extension appex. Without this, a dev-signed iOS app runs
on an Apple silicon Mac but its packet-tunnel extension is refused with
`AMFI -413 No matching profile found` — Xcode's run-on-Mac action is the only user-space writer.

It ships targeted at **Connect** (`com.vpnhood.connect.ios` + "VpnHood Connect Dev Profile" pair).

## Use (once per profile regeneration, ~yearly)

1. Open `Shim.xcodeproj` in Xcode, destination **My Mac (Designed for iPad)**, **⌘R**.
2. When the stub window appears, press **Stop (■)** in Xcode — its debug session squats on the
   bundle id and blocks the real app's launch (`open` fails with -1712; SIGKILL won't clear it).
3. Delete `~/Library/Developer/Xcode/DerivedData/Shim-*`, then re-register the real appex:
   `pluginkit -a "<wrapper>/Wrapper/<app>.app/PlugIns/<ext>.appex"`
   (verify with `pluginkit -m -v -i <ext-bundle-id>`).

## Retarget for Client

```bash
sed -i '' -e 's/com\.vpnhood\.connect\.ios/com.vpnhood.client.ios/g' \
  -e 's/VpnHood Connect Dev Profile/VpnHood Client Dev Profile/' \
  -e 's/VpnHood Connect Extension Dev Profile/VpnHood Client Extension Dev Profile/' \
  Shim.xcodeproj/project.pbxproj
sed -i '' 's/group\.com\.vpnhood\.connect\.ios/group.com.vpnhood.client.ios/' \
  ShimApp/ShimApp.entitlements ShimExt/ShimExt.entitlements
plutil -remove "com\.apple\.developer\.applesignin" ShimApp/ShimApp.entitlements  # Client has no SIWA
```

Full run-on-Mac procedure: [`docs/ios/build-deploy-and-provisioning.md`](../../../docs/ios/build-deploy-and-provisioning.md)
§ "Run on a Mac (Apple silicon)".
