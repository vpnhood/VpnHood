$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));

& "$SolutionDir/Pub/Core/vh_installer/publish_impl.ps1" `
	-projectDir $PSScriptRoot `
	-repoBaseUrl "https://github.com/vpnhood/VpnHood.App.Server" `
	-publishDirName "VpnHoodServer" `
	-os "win" `
	-cpu "x64" `
	-launcherName "vhserver";
exit;

& "$SolutionDir/Pub/Core/vh_installer/publish.ps1" `
	-projectDir $PSScriptRoot `
	-repoBaseUrl "https://github.com/vpnhood/VpnHood.App.Server" `
	-publishDirName "VpnHoodServer" `
	-os "linux" `
	-launcherName "vhserver";

