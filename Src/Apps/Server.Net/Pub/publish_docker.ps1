param( [Parameter(Mandatory=$true)][object]$distribute );

$SolutionDir = Split-Path -Parent (Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot)));
$distribute = $distribute -eq "1";

Write-Host;
Write-Host "*** Creating Docker..." -BackgroundColor Blue -ForegroundColor White;

# Init script
$projectDir = Split-Path $PSScriptRoot -Parent;
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;
. "$SolutionDir/Pub/Core/Common.ps1";

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
$linuxScript = (Get-Content -Path "$template_installerFile" -Raw).Replace('$composeUrlParam', "https://github.com/vpnhood/VpnHood.App.Server/releases/download/$versionTag/$module_yamlFileName");
$linuxScript = $linuxScript -replace "`r`n", "`n";
$linuxScript | Out-File -FilePath "$module_installerFile" -Encoding ASCII -Force -NoNewline;

# copy compose file
Copy-Item -path "$template_yamlFile" -Destination "$module_yamlFile" -Force;

# remove old docker containers from local
$serverDockerImage="vpnhood/vpnhoodserver";
$oldContainers = docker ps -a -q --filter "ancestor=$serverDockerImage";
$oldImages = docker images -a -q "$serverDockerImage";

echo "removing old docker containers and images..."
if ($oldContainers) { docker rm -vf $oldContainers; }
if ($oldImages) { docker rmi -f $oldImages; }

# multi-arch build. The Dockerfile is architecture-agnostic (portable .NET + arch-aware
# libmsquic via apt/dnf), so a single buildx invocation produces both amd64 and arm64.
$platforms = "linux/amd64,linux/arm64";

# tags: always the version tag; add :latest only when this is the latest release
$tagArgs = @("-t", "${serverDockerImage}:$versionTag");
if ($isLatest) { $tagArgs += @("-t", "${serverDockerImage}:latest"); }

# multi-arch needs the docker-container driver (the default 'docker' driver cannot build
# multiple platforms). Reuse a dedicated builder, creating it on first run.
docker buildx inspect vhbuilder *> $null;
if (!$?) { docker buildx create --name vhbuilder --driver docker-container --use | Out-Null; }
else { docker buildx use vhbuilder | Out-Null; }

# ensure QEMU emulators are registered so the arm64 image can be built on an amd64 host
docker run --privileged --rm tonistiigi/binfmt --install arm64 *> $null;

if ($distribute)
{
	# a multi-arch manifest cannot be loaded into the local daemon; it must be pushed directly
	docker buildx build "$solutionDir" --no-cache --platform $platforms -f "$projectDir/Dockerfile" @tagArgs --push;
	if (!$?) { Throw("Could not build/push the server docker image."); }
	echo "The server docker image (amd64 + arm64) has been pushed."
}
else
{
	# local build: a multi-arch manifest can't be --load'ed, so build the host arch only for testing
	docker buildx build "$solutionDir" --no-cache -f "$projectDir/Dockerfile" @tagArgs --load;
	if (!$?) { Throw("Could not build the server docker."); }
}

if ($isLatest)
{
	Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse;
}
