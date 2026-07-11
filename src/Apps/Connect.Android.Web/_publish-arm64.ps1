$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/pub/Lib/PublishAndroidApp.ps1" $PSScriptRoot  `
	-appFolder "VpnHoodConnect" `
	-distribution "web-arm64" `
	-connect `
	-archs "android-arm64" `
	-apk;
