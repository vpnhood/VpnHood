$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/pub/lib/Publish-AndroidApp.ps1" $PSScriptRoot  `
	-appFolder "VpnHoodClient" `
	-distribution "web" `
	-apk;

