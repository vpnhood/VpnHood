$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/pub/lib/vh-installer/Publish-Installer.ps1" `
	-projectDir $PSScriptRoot `
	-publishDirName "VpnHoodClient" `
	-os "linux" `
	-launcherName "vhclient";
