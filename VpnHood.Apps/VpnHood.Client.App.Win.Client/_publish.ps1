& "$PSScriptRoot/../../Pub/Core/PublishWinApp.ps1" `
	-projectDir $PSScriptRoot  `
	-packageFileTitle "VpnHoodClient" `
	-aipFileR "VpnHood.Apps/VpnHood.Client.App.Win.Client.Setup/VpnHood.Client.App.Win.Client.Setup.aip" `
	-distribution "web" `
	-repoBaseUrl "https://github.com/vpnhood/VpnHood" `
	-installationPageUrl "https://github.com/vpnhood/VpnHood/wiki/Install-VpnHood-Client"