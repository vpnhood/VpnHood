param(
	[Parameter(Mandatory=$true)][object]$distribute,
	# CI mode: the docker-compose helper files (VpnHoodServer.docker.yml/.sh) and the image's build
	# context (docker-context) only, no image build/push - the workflow builds and pushes the
	# multi-arch image from that context with docker/build-push-action. Local runs never set this.
	[object]$generateOnly = "0"
	);

$SolutionDir = Split-Path -Parent (Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot)));
$distribute = $distribute -eq "1";
$generateOnly = $generateOnly -eq "1";

Write-Host;
Write-Host "*** Creating Docker..." -BackgroundColor Blue -ForegroundColor White;

# Init script
$projectDir = Split-Path $PSScriptRoot -Parent;
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;
. "$SolutionDir/pub/lib/Common.ps1";

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

# Release repo the compose file is downloaded from. In CI (VH_PUBLISH_REPO / GITHUB_REPOSITORY set)
# this is the release repo the workflow runs in, so a fork points at its own; locally it falls back to
# the canonical repo. Common.ps1 (sourced above) provides Resolve-PublishRepoUrl.
$repoBaseUrl =
	if (-not [string]::IsNullOrWhiteSpace($env:VH_PUBLISH_REPO) -or -not [string]::IsNullOrWhiteSpace($env:GITHUB_REPOSITORY)) {
		Resolve-PublishRepoUrl;
	}
	else {
		"https://github.com/vpnhood/VpnHood.App.Server";
	}

# server VpnHoodServer.docker.sh
Write-Output "Make Server installation script for this docker";
$linuxScript = (Get-Content -Path "$template_installerFile" -Raw).Replace('$composeUrlParam', "$repoBaseUrl/releases/download/$versionTag/$module_yamlFileName");
$linuxScript = $linuxScript -replace "`r`n", "`n";
$linuxScript | Out-File -FilePath "$module_installerFile" -Encoding ASCII -Force -NoNewline;

# copy compose file
Copy-Item -path "$template_yamlFile" -Destination "$module_yamlFile" -Force;

# The image's build context, not the source (see the Dockerfile): the release's Linux packages and
# install scripts, one folder per Docker architecture, beside the two scripts the Dockerfile runs.
# The packages come first: pub/Server/Publish.ps1 locally, the workflow's build job in CI.
$serverDockerImage = "vpnhood/vpnhoodserver";
$contextDir = "$packagesRootDir/$packageServerDirName/docker-context";
Remove-Item -Path $contextDir -Recurse -Force -ErrorAction Ignore;
foreach ($arch in @("amd64", "arm64")) {
	$rid = if ($arch -eq "amd64") { "linux-x64" } else { "linux-arm64" };
	$packageFile = "$packagesRootDir/$packageServerDirName/$rid/$packageServerDirName-$rid.tar.gz";
	if (-not (Test-Path $packageFile)) { Throw "The $rid package is missing; build it first: pub/Server/Publish.ps1"; }
	New-Item -ItemType Directory -Path "$contextDir/$arch" -Force | Out-Null;
	Copy-Item -Path $packageFile -Destination "$contextDir/$arch/package.tar.gz";
	Copy-Item -Path ($packageFile -replace '\.tar\.gz$', '.sh') -Destination "$contextDir/$arch/install.sh";
}
# bash in the image reads LF only, whatever line endings the checkout has
foreach ($script in @("$templateDir/vhsupervisor.sh", "$PSScriptRoot/Linux/install-msquic.sh")) {
	$text = (Get-Content -Path $script -Raw) -replace "`r`n", "`n";
	[IO.File]::WriteAllText("$contextDir/$(Split-Path $script -Leaf)", $text);
}

# CI generate-only mode: the compose helper files and the build context are done; the workflow's
# docker/build-push-action builds and pushes the multi-arch image from that context.
if ($generateOnly) {
	if ($isLatest) {
		Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse;
	}
	Write-Host "generateOnly: compose files and the build context written; skipping local docker build." -ForegroundColor Yellow;
	return;
}

# multi-arch build: one buildx run makes both amd64 and arm64, each from its own package
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
	docker buildx build "$contextDir" --pull --no-cache --platform $platforms -f "$projectDir/Dockerfile" @tagArgs --push;
	if (!$?) { Throw("Could not build/push the server docker image."); }
	echo "The server docker image (amd64 + arm64) has been pushed."
}
else
{
	# local build: a multi-arch manifest can't be --load'ed, so build the host arch only for testing
	docker buildx build "$contextDir" --pull --no-cache -f "$projectDir/Dockerfile" @tagArgs --load;
	if (!$?) { Throw("Could not build the server docker."); }
}

if ($isLatest)
{
	Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse;
}
