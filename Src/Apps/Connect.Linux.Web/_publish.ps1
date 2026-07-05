$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/Pub/Lib/vh_installer/publish.ps1" `
	-projectDir $PSScriptRoot `
	-publishDirName "VpnHoodConnect" `
	-os "linux" `
	-launcherName "vhconnect" `
	-connect;
