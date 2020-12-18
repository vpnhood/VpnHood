$packageName = "VpnHoodClient-win";

. "$PSScriptRoot\..\Pub\PublishApp.ps1" `
	$PSScriptRoot `
	-withVbsLauncher `
	-packageName "$packageName" `
	-updateUrl "https://github.com/vpnhood/VpnHood/releases/latest/download/$packageName.json" `
	-packageDownloadUrl "https://github.com/vpnhood/VpnHood/releases/latest/download/$packageName.zip" `
