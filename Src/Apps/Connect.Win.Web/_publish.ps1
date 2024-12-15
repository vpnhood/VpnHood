$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/Pub/Core/PublishWinApp.ps1" `
	-projectDir $PSScriptRoot  `
	-packageFileTitle "VpnHoodConnect" `
	-aipFileR "Src/Apps/VpnHood.AppLib.Win.Connect.Setup/VpnHood.AppLib.Win.Connect.Setup.aip" `
	-distribution "web" `
	-repoBaseUrl "https://github.com/vpnhood/VpnHood.AppLib.Connect" `
	-installationPageUrl "https://www.vpnhood.com"