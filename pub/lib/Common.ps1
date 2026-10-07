$ErrorActionPreference = "Stop";

$solutionDir = Split-Path -parent (Split-Path -parent $PSScriptRoot);
$gitDir = "$solutionDir/.git";   # consumed by pub/Invoke-VersionBump.ps1's git commit/push (dot-sources this)
$pubDir = "$solutionDir/pub";
if ($env:ProgramFiles) {
	$msbuild = Join-Path ${Env:Programfiles} "Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe";
}
# The NuGet key is never stored: nuget.org Trusted Publishing hands CI a short-lived one (the NuGet/login
# step), passed in as NUGET_API_KEY. Without it -> empty: a fork still builds, and only a push throws.
$nugetApiKey = "$env:NUGET_API_KEY".Trim();
$msverbosity = "minimal";

# Version (READ-ONLY). Common never mutates the version — it only reads PubVersion.json and derives
# $versionTag/$versionParam/$prerelease/$isLatest/$releaseFlag. The single place the version is
# incremented is pub/Invoke-VersionBump.ps1 (it calls Update-VersionFile.ps1 -bump directly, before sourcing this).
. "$PSScriptRoot/Update-VersionFile.ps1" -versionFile "$pubDir/PubVersion.json" -bump 0;

# Packages Directory
$packagesRootDir = "$pubDir/bin/" + $versionTag;
$packageServerDirName = "VpnHoodServer";
$packageClientDirName = "VpnHoodClient";
$packageConnectDirName = "VpnHoodConnect";

# Load the publish-repo + app-config helpers used by the publish scripts:
#   Resolve-PublishRepoSlug / Resolve-PublishRepoUrl [-Connect] — resolve the target repo (defaults to
#     the current repo so a fork publishes to itself; override with VH_PUBLISH_REPO / VH_CONNECT_PUBLISH_REPO).
#   Get-AppPublishConfig — per-app .user/<appFolder>/ config lookups.
#   Get-LegacyAndroidInfoFileName — retired asset names still emitted during a rename's grace period.
# Callers invoke these directly (e.g. Publish-GithubRelease gets its repo as a param resolved by the caller).
. "$PSScriptRoot/Resolve-PublishRepo.ps1";
. "$PSScriptRoot/AppPublishConfig.ps1";
. "$PSScriptRoot/LegacyAssetAliases.ps1";

# Prepare the latest folder
$packagesRootDirLatest = "$pubDir/bin/latest";

# Release root such as latet or pre-release folder
$releaseRootDir = (&{if($isLatest) {$packagesRootDirLatest} else {$packagesRootDir}})

# A project property as MSBuild evaluates it: its imports, the app's identity and every condition
# included. The csproj's XML shows only what is written there, and a head takes its id and names from
# the app's identity (src/Apps/<Product>/Directory.Build.props, VpnHood.AppLib.App.targets), writing
# $(VhAppPackageTitle) where its name used to be, or nothing at all.
function Get-ProjectProperty([string]$projectFile, [string]$name, [string]$configuration = "Release")
{
	$value = & dotnet msbuild $projectFile -nologo "-getProperty:$name" "-p:Configuration=$configuration";
	if ($LASTEXITCODE -ne 0) { Throw "Could not evaluate $name of ${projectFile}: $value"; }
	return "$value".Trim();
}

function PrepareModuleFolder([string]$moduleDir, [string]$moduleDirLatest)
{
	# Remove old files
	try { Remove-Item -path "$moduleDir" -Force -Recurse } catch {}
	New-Item -ItemType Directory -Path $moduleDir -Force | Out-Null;

	if ($isLatest)
	{
		try { Remove-Item -path $moduleDirLatest -Force -Recurse } catch {}
		New-Item -ItemType Directory -Path $moduleDirLatest -Force | Out-Null;
	}
}