param([switch]$pushDocker)

. "$PSScriptRoot\..\Pub\Common.ps1"
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


# server VpnHoodServer.docker.sh
$linuxScript = (Get-Content -Path "$PSScriptRoot/Install/VpnHoodServer.docker.sh" -Raw).Replace('$composeUrlParam', "https://github.com/vpnhood/VpnHood/releases/download/$versionTag/VpnHoodServer.docker.yml");
$linuxScript = $linuxScript -replace "`r`n", "`n";
$linuxScript  | Out-File -FilePath "$packagesServerDir/VpnHoodServer.docker.sh" -Encoding ASCII -Force -NoNewline;

# copy compose file
Copy-Item -path "$projectDir\Install\VpnHoodServer.docker.yml" -Destination "$packagesServerDir\" -Force

if ($pushDocker)
{
	# remove old docker containers from local
	$serverDockerImage="vpnhood/vpnhoodserver";
	docker rm -vf $(docker ps -a -q --filter "ancestor=$serverDockerImage")
	docker rmi -f $(docker images -a -q "$serverDockerImage")

	# create name image
	docker build "$solutionDir" -f "$projectDir\Dockerfile" -t ${serverDockerImage}:latest -t ${serverDockerImage}:$versionTag
	docker push ${serverDockerImage}:latest
	docker push ${serverDockerImage}:$versionTag
}