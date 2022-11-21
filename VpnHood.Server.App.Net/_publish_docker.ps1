param( [Parameter(Mandatory=$true)][object]$distribute );
. "$PSScriptRoot/../Pub/Common.ps1";


Write-Host;
Write-Host "*** Creating Docker..." -BackgroundColor Blue -ForegroundColor White;

$projectDir = $PSScriptRoot;
$packageName = "VpnHoodServer";
$ymlFileName = "VpnHoodServer.docker.yml";
$moduleInstallFilename = "VpnHoodServer.docker.sh";
$moduleDir = "$packagesServerDir/docker";
$moduleDirLatest = "$packagesServerDirLatest/docker";

# prepare module folders
PrepareModuleFolder $moduleDir $moduleDirLatest;

# server VpnHoodServer.docker.sh
echo "Make Server installation script for this docker";
$linuxScript = (Get-Content -Path "$PSScriptRoot/Install/$moduleInstallFilename" -Raw).Replace('$composeUrlParam', "https://github.com/vpnhood/VpnHood/releases/download/$versionTag/$ymlFileName");
$linuxScript = $linuxScript -replace "`r`n", "`n";
$linuxScript  | Out-File -FilePath "$moduleDir/$moduleInstallFilename" -Encoding ASCII -Force -NoNewline;

# copy compose file
Copy-Item -path "$projectDir/Install/$ymlFileName" -Destination "$moduleDir/" -Force;

# remove old docker containers from local
$serverDockerImage="vpnhood/vpnhoodserver";
docker rm -vf $(docker ps -a -q --filter "ancestor=$serverDockerImage");
docker rmi -f $(docker images -a -q "$serverDockerImage");

# create name image
docker build "$solutionDir" -f "$projectDir/Dockerfile" -t ${serverDockerImage}:latest -t ${serverDockerImage}:$versionTag;
if ($isLatest)
{
	if ($distribute)
	{
		#docker push ${serverDockerImage}:latest;
		#docker push ${serverDockerImage}:$versionTag;
	}
	Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse;
}
else
{
	if ($distribute)
	{
		docker push ${serverDockerImage}:$versionTag;
	}
}
