$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/pub/lib/Publish-AndroidApp.ps1" $PSScriptRoot  `
	-appFolder "VpnHoodConnect" `
	-distribution "google" `
	-connect `
	-aab;
