$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/Pub/Core/PublishAndroidApp.ps1" $PSScriptRoot  `
	-packageFileTitle "VpnHoodClient" `
	-packageId "com.vpnhood.client.android" `
	-distribution "google" `
	-repoUrl "https://github.com/vpnhood/VpnHood" `
	-aab;

