param( [Parameter(Mandatory=$true)][object]$distribute );
$distribute = $distribute -eq "1";

. "$PSScriptRoot/../Pub/Common.ps1";
$projectDir = $PSScriptRoot;

Write-Host;
Write-Host "*** Creating Docker..." -BackgroundColor Blue -ForegroundColor White;

# prepare module folders
$moduleDir = "$packagesServerDir/docker";
$moduleDirLatest = "$packagesServerDirLatest/docker";
PrepareModuleFolder $moduleDir $moduleDirLatest;

$templateDir = "$projectDir/Install/Linux-Docker/";
$template_installerFile = "$templateDir/install.sh";
$template_yamlFile = "$templateDir/compose.yml";

$module_yamlFile = "$moduleDir/VpnHoodServer.docker.yml";
$module_installerFile = "$moduleDir/VpnHoodServer.docker.sh";

# Calcualted Path
$module_yamlFileName = $(Split-Path "$module_yamlFile" -leaf);

# server VpnHoodServer.docker.sh
Write-Output "Make Server installation script for this docker";
$linuxScript = (Get-Content -Path "$template_installerFile" -Raw).Replace('$composeUrlParam', "https://github.com/vpnhood/VpnHood/releases/download/$versionTag/$module_yamlFileName");
$linuxScript = $linuxScript -replace "`r`n", "`n";
$linuxScript  | Out-File -FilePath "$module_installerFile" -Encoding ASCII -Force -NoNewline;

# copy compose file
Copy-Item -path "$template_yamlFile" -Destination "$module_yamlFile" -Force;

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
