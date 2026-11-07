$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot)));
# ARM only: Galaxy Store reaches Samsung's phones and tablets, all ARM, and x64 would be a third of the APK.
# 32-bit arm stays for the old phones.
& "$SolutionDir/pub/lib/Publish-AndroidApp.ps1" $PSScriptRoot  `
	-appFolder "VpnHoodClient" `
	-distribution "samsung" `
	-archs "android-arm64;android-arm" `
	-noUpdate `
	-apk;

