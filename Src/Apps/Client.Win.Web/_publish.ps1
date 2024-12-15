$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/Pub/Core/PublishWinApp.ps1" `
	-projectDir $PSScriptRoot  `
	-packageFileTitle "VpnHoodClient" `
	-aipFileR "Src/Apps/VpnHood.AppLib.Win.Client.Setup/VpnHood.AppLib.Win.Client.Setup.aip" `
	-distribution "web" `
	-repoBaseUrl "https://github.com/vpnhood/VpnHood" `
	-installationPageUrl "https://github.com/vpnhood/VpnHood/wiki/Install-VpnHood-Client"