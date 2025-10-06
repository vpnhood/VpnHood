$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/Pub/Core/vh_installer/publish.ps1" `
	-projectDir $PSScriptRoot `
	-repoBaseUrl "https://github.com/vpnhood/VpnHood.App.Connect" `
	-publishDirName "VpnHoodConnect" `
	-os "linux" `
	-launcherName "vhconnect"
	-autoLaunch $true;
