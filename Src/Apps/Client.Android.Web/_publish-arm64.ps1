$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/Pub/Core/PublishAndroidApp.ps1" $PSScriptRoot  `
	-packageFileTitle "VpnHoodClient" `
	-packageId "com.vpnhood.client.android.web" `
	-distribution "arm64-web" `
	-repoUrl "https://github.com/vpnhood/VpnHood.App.Connect" `
	-archs "android-arm64" `
	-apk;
