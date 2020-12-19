$packageName = "VpnHood-AccessServer";

. "$PSScriptRoot\..\..\VpnHood\Pub\PublishApp.ps1" `
	-projectDir $PSScriptRoot -withLauncher `
	-packageName $packageName
