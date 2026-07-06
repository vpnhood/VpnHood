$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/Pub/Lib/PublishIosApp.ps1" $PSScriptRoot `
	-appFolder "VpnHoodClient" `
	-distribution "ios";
