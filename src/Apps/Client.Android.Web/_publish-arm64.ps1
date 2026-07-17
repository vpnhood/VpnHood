$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/pub/lib/Publish-AndroidApp.ps1" $PSScriptRoot  `
	-appFolder "VpnHoodClient" `
	-distribution "web-arm64" `
	-archs "android-arm64" `
	-apk;
