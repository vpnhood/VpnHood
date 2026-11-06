$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot)));
# The website's app with no updater, for IzzyOnDroid (issue 803), which takes arm64 only.
& "$SolutionDir/pub/lib/Publish-AndroidApp.ps1" $PSScriptRoot  `
	-appFolder "VpnHoodClient" `
	-distribution "webnoupdate-arm64" `
	-archs "android-arm64" `
	-noUpdate `
	-apk;
