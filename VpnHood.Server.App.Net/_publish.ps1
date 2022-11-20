. "$PSScriptRoot\..\Pub\Common.ps1";

Write-Host;
Write-Host "*** Building Server ..." -BackgroundColor Blue -ForegroundColor White;

$packageName = "VpnHoodServer";
$linuxInstallFileName = "install-linux.sh";

# Remove old files
try { Remove-Item -path "$packagesServerDir/$linuxInstallFileName" -force | Out-Null; } catch {}

# server install-linux.sh
echo "Make Server installation script for this release"
$linuxScript = (Get-Content -Path "$PSScriptRoot/Install/$linuxInstallFileName" -Raw).Replace('$installUrlParam', "https://github.com/vpnhood/VpnHood/releases/download/$versionTag/VpnHoodServer.tar.gz");
$linuxScript = $linuxScript -replace "`r`n", "`n";
$linuxScript  | Out-File -FilePath "$packagesServerDir/$linuxInstallFileName" -Encoding ASCII -Force -NoNewline;

. "$PSScriptRoot/../Pub/PublishApp.ps1" `
	$PSScriptRoot `
	-withLauncher `
	-packagesDir $packagesServerDir `
	-packageName $packageName `
	-updateUrl "https://github.com/vpnhood/VpnHood/releases/latest/download/$packageName.json" `
	-packageDownloadUrl "https://github.com/vpnhood/VpnHood/releases/latest/download/$packageName.zip"
