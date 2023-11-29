param( [Parameter(Mandatory=$true)][object]$distribute );
$distribute = $distribute -eq "1";

Write-Host;
Write-Host "*** Creating Docker..." -BackgroundColor Blue -ForegroundColor White;

# Init script
$projectDir = Split-Path $PSScriptRoot -Parent;
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;
. "$projectDir/../Pub/Core/Common.ps1";

#update project version
UpdateProjectVersion $projectFile;

# prepare module folders
$moduleDir = "$packagesRootDir/$packageServerDirName/docker";
$moduleDirLatest = "$packagesRootDirLatest/$packageServerDirName/docker";
PrepareModuleFolder $moduleDir $moduleDirLatest;

$templateDir = "$PSScriptRoot/Linux-Docker/";
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
$oldContainers = docker ps -a -q --filter "ancestor=$serverDockerImage";
$oldImages = docker images -a -q "$serverDockerImage";

echo "removing old docker containers and images..."
if ($oldContainers) { docker rm -vf $oldContainers; }
if ($oldImages) { docker rmi -f $oldImages; }

# create name image
docker build "$solutionDir" --no-cache -f "$projectDir/Dockerfile" -t ${serverDockerImage}:latest -t ${serverDockerImage}:$versionTag;
if (!$?) {Throw("Could not buld the server docker."); }

if ($isLatest)
{
	if ($distribute)
	{
		docker push ${serverDockerImage}:$versionTag;
		docker push ${serverDockerImage}:latest;
		if (!$?) { Throw("Could not push the server docker image."); }
		echo "The server docker image has been pushed."
	}
	Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse;
}
else
{
	if ($distribute)
	{
		docker push ${serverDockerImage}:$versionTag;
		if (!$?) { Throw("Could not push the server docker image."); }
		echo "The server docker image has been pushed."
	}
}
