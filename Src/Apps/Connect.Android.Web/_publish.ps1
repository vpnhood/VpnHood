$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/Pub/Core/PublishAndroidApp.ps1" $PSScriptRoot  `
	-packageFileTitle "VpnHoodConnect" `
	-packageId "com.vpnhood.connect.android.web" `
	-distribution "web" `
	-repoUrl "https://github.com/vpnhood/VpnHood.App.Connect" `
	-apk;
