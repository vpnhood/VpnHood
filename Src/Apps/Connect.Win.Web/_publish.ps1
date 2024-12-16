$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/Pub/Core/PublishWinApp.ps1" `
	-projectDir $PSScriptRoot  `
	-packageFileTitle "VpnHoodConnect" `
	-aipFileR "Src/Apps/Connect.Win.Web.Setup/VpnHood.App.Connect.Win.Web.Setup.aip" `
	-distribution "web" `
	-repoBaseUrl "https://github.com/vpnhood/VpnHood.App.Connect" `
	-installationPageUrl "https://www.vpnhood.com"