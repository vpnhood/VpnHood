param([switch]$prerelease)
$projectDir = $PSScriptRoot;

. "$PSScriptRoot\..\Pub\Common.ps1" -prerelease:$prerelease
$packageName = "VpnHoodServer";
New-Item -Path $packagesServerDir -ItemType Directory -Force;

# server VpnHoodServer.docker.sh
echo "Make Server installation script for this docker"
$linuxScript = (Get-Content -Path "$PSScriptRoot/Install/VpnHoodServer.docker.sh" -Raw).Replace('$composeUrlParam', "https://github.com/vpnhood/VpnHood/releases/download/$versionTag/VpnHoodServer.docker.yml");
$linuxScript = $linuxScript -replace "`r`n", "`n";
$linuxScript  | Out-File -FilePath "$packagesServerDir/VpnHoodServer.docker.sh" -Encoding ASCII -Force -NoNewline;

# copy compose file
Copy-Item -path "$projectDir\Install\VpnHoodServer.docker.yml" -Destination "$packagesServerDir\" -Force

# remove old docker containers from local
$serverDockerImage="vpnhood/vpnhoodserver";
docker rm -vf $(docker ps -a -q --filter "ancestor=$serverDockerImage")
docker rmi -f $(docker images -a -q "$serverDockerImage")

# create name image
docker build "$solutionDir" -f "$projectDir\Dockerfile" -t ${serverDockerImage}:latest -t ${serverDockerImage}:$versionTag
if ($prerelease)
{
	docker push -t ${serverDockerImage}:$versionTag
}
else
{
	#docker push ${serverDockerImage}:latest
	#docker push ${serverDockerImage}:$versionTag
}
