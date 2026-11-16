$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot)));
# The website's app with no updater, for IzzyOnDroid (issue 803), which takes arm64 only. Its own feed
# gives IzzyOnDroid's client 10 days to update it before the app's notice comes.
& "$SolutionDir/pub/lib/Publish-AndroidApp.ps1" $PSScriptRoot  `
	-appFolder "VpnHoodClient" `
	-distribution "fdroid-arm64" `
	-archs "android-arm64" `
	-noUpdate `
	-feedNotificationDelay "10.00:00:00" `
	-apk;
