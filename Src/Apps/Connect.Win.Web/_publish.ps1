$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/Pub/Lib/PublishWinApp.ps1" `
	-projectDir $PSScriptRoot  `
	-appFolder "VpnHoodConnect" `
	-aipFileR "Src/Apps/Connect.Win.Web.Setup/VpnHood.App.Connect.Win.Web.Setup.aip" `
	-distribution "web" `
	-connect