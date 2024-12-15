$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/Pub/Core/PublishWinApp.ps1" `
	-projectDir $PSScriptRoot  `
	-packageFileTitle "VpnHoodConnect" `
	-aipFileR "VpnHood.Apps/VpnHood.AppLibs.Win.Connect.Setup/VpnHood.AppLibs.Win.Connect.Setup.aip" `
	-distribution "web" `
	-repoBaseUrl "https://github.com/vpnhood/VpnHood.AppLibs.Connect" `
	-installationPageUrl "https://www.vpnhood.com"