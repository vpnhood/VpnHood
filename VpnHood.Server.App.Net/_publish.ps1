param([switch]$ftp) 

$packageName = "VpnHoodServer";
. "$PSScriptRoot/../Pub/PublishApp.ps1" `
	$PSScriptRoot `
	-withLauncher `
	-ftp:$ftp `
	-packagesDir $packagesServerDir `
	-packageName $packageName `
	-updateUrl "https://github.com/vpnhood/VpnHood/releases/latest/download/$packageName.json" `
	-packageDownloadUrl "https://github.com/vpnhood/VpnHood/releases/latest/download/$packageName.zip"

# copy install folder
echo "$outDir/Install"
echo "$publishDir/install"
Move-Item -path "$outDir/Install" -Destination "$publishDir/install" -Force
