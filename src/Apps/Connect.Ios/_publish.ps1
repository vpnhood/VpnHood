$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/pub/Lib/PublishIosApp.ps1" $PSScriptRoot `
	-appFolder "VpnHoodConnect" `
	-distribution "ios" `
	-connect;
