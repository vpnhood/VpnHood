$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/pub/lib/Publish-IosApp.ps1" $PSScriptRoot `
	-appFolder "VpnHoodClient" `
	-distribution "ios";
