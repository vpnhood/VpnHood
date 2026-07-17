$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/pub/lib/Publish-AndroidApp.ps1" $PSScriptRoot  `
	-appFolder "VpnHoodConnect" `
	-distribution "web-arm64" `
	-connect `
	-archs "android-arm64" `
	-apk;
