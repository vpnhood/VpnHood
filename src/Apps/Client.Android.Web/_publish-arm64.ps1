$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/pub/Lib/PublishAndroidApp.ps1" $PSScriptRoot  `
	-appFolder "VpnHoodClient" `
	-distribution "web-arm64" `
	-archs "android-arm64" `
	-apk;
