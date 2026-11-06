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
# $(VhAppPackageTitle) where its name used to be, or nothing at all. $framework: one target of a head with
# several (Client.Ios.Apple's iOS and tvOS), whose platform's properties exist only in that target's build.
function Get-ProjectProperty([string]$projectFile, [string]$name, [string]$configuration = "Release", [string]$framework = "")
{
	$msbuildArgs = @($projectFile, "-nologo", "-getProperty:$name", "-p:Configuration=$configuration");
	if ($framework) { $msbuildArgs += "-p:TargetFramework=$framework"; }
	$value = & dotnet msbuild @msbuildArgs;
	if ($LASTEXITCODE -ne 0) { Throw "Could not evaluate $name of ${projectFile}: $value"; }
	return "$value".Trim();
}

# Throws if an APK asks for a permission. An APK's manifest is binary, with its strings in UTF-16 or
# UTF-8, so the name is looked for in both, and no Android tool has to be on the path. INTERNET, which
# every build of ours has, proves the search still finds a name: a guard that can no longer read the
# manifest must not pass.
function Assert-ApkLacksPermission([string]$apkFile, [string]$permission)
{
	Add-Type -AssemblyName System.IO.Compression.FileSystem;
	$zip = [System.IO.Compression.ZipFile]::OpenRead($apkFile);
	try {
		$entry = $zip.GetEntry("AndroidManifest.xml");
		if (-not $entry) { Throw "$apkFile has no AndroidManifest.xml."; }
		$memory = [System.IO.MemoryStream]::new();
		$stream = $entry.Open();
		try { $stream.CopyTo($memory); } finally { $stream.Dispose(); }
	}
	finally { $zip.Dispose(); }

	# Latin-1 maps each byte to one char, so a plain search compares raw bytes
	$latin1 = [System.Text.Encoding]::GetEncoding(28591);
	$manifest = $latin1.GetString($memory.ToArray());
	$hasName = {
		param([string]$name)
		$manifest.Contains($name) -or $manifest.Contains($latin1.GetString([System.Text.Encoding]::Unicode.GetBytes($name)));
	};

	if (-not (& $hasName "android.permission.INTERNET")) { Throw "Could not read the permissions of $apkFile."; }
	if (& $hasName $permission) { Throw "$apkFile must not ask for $permission."; }
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