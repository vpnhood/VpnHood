$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/Pub/Lib/PublishAndroidApp.ps1" $PSScriptRoot  `
	-appFolder "VpnHoodConnect" `
	-distribution "web" `
	-connect `
	-apk;
