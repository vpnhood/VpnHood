. "$PSScriptRoot\..\Pub\Common.ps1";
$packageName = "VpnHoodServer";
New-Item -Path $packagesServerDir -ItemType Directory -Force

# server install-linux.sh
echo "Make Server installation script for this release"
$linuxScript = (Get-Content -Path "$PSScriptRoot/Install/install-linux.sh" -Raw).Replace('$installUrlParam', "https://github.com/vpnhood/VpnHood/releases/download/$versionTag/VpnHoodServer.tar.gz");
$linuxScript = $linuxScript -replace "`r`n", "`n";
$linuxScript  | Out-File -FilePath "$packagesServerDir/install-linux.sh" -Encoding ASCII -Force -NoNewline;

. "$PSScriptRoot/../Pub/PublishApp.ps1" `
	$PSScriptRoot `
	-withLauncher `
	-packagesDir $packagesServerDir `
	-packageName $packageName `
	-updateUrl "https://github.com/vpnhood/VpnHood/releases/latest/download/$packageName.json" `
	-packageDownloadUrl "https://github.com/vpnhood/VpnHood/releases/latest/download/$packageName.zip"
