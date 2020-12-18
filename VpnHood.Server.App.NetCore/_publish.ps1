$packageName = "VpnHoodServer";

. "$PSScriptRoot\..\Pub\PublishApp.ps1" `
	$PSScriptRoot -withLauncher `
	-packageName "$packageName" `
	-updateUrl "https://github.com/vpnhood/VpnHood/releases/latest/download/$packageName.json" `
	-packageDownloadUrl "https://github.com/vpnhood/VpnHood/releases/latest/download/$packageName.zip" `

