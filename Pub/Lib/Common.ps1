$ErrorActionPreference = "Stop";

$solutionDir = Split-Path -parent (Split-Path -parent $PSScriptRoot);
$gitDir = "$solutionDir/.git";   # consumed by Pub/Bump.ps1's git commit/push (dot-sources this)
$pubDir = "$solutionDir/Pub";
if ($env:ProgramFiles) {
	$msbuild = Join-Path ${Env:Programfiles} "Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe";
}
$userDir = "$solutionDir/../.user";
# Secrets live as discrete files under .user/ (one value per file) so they map 1:1 to GitHub
# secrets. Android keystores/passwords resolve per-app in PublishAndroidApp.ps1; here we only
# need the global NuGet token. Missing file -> empty (a fork without secrets still builds).
# Wrap in "$(...)" so an empty file (Get-Content -Raw returns $null) coerces to "" instead of
# throwing "cannot call a method on a null-valued expression" — keeps a keyless fork green.
$nugetApiKey = if (Test-Path "$userDir/nuget_apikey.txt") { "$(Get-Content "$userDir/nuget_apikey.txt" -Raw)".Trim() } else { "" };
$msverbosity = "minimal";

# Version (READ-ONLY). Common never mutates the version — it only reads PubVersion.json and derives
# $versionTag/$versionParam/$prerelease/$isLatest/$releaseFlag. The single place the version is
# incremented is Pub/Bump.ps1 (it calls VersionBump.ps1 -bump directly, before sourcing this).
. "$PSScriptRoot/VersionBump.ps1" -versionFile "$pubDir/PubVersion.json" -bump 0;

# Packages Directory
$packagesRootDir = "$pubDir/bin/" + $versionTag;
$packageServerDirName = "VpnHoodServer";
$packageClientDirName = "VpnHoodClient";
$packageConnectDirName = "VpnHoodConnect";

# Load the publish-repo + app-config helpers used by the publish scripts:
#   Resolve-PublishRepoSlug / Resolve-PublishRepoUrl [-Connect] — resolve the target repo (defaults to
#     the current repo so a fork publishes to itself; override with VH_PUBLISH_REPO / VH_CONNECT_PUBLISH_REPO).
#   Get-AppPublishConfig — per-app .user/<packageFileTitle>/ config lookups.
# Callers invoke these directly (e.g. PublishToGithub gets its repo as a param resolved by the caller).
. "$PSScriptRoot/ResolvePublishRepo.ps1";
. "$PSScriptRoot/AppPublishConfig.ps1";

# Prepare the latest folder
$packagesRootDirLatest = "$pubDir/bin/latest";

# Release root such as latet or pre-release folder
$releaseRootDir = (&{if($isLatest) {$packagesRootDirLatest} else {$packagesRootDir}})

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

# push to repo using gh api.
# Do not show any message except error
function PushTextToRepo {
    param(
        [Parameter(Mandatory=$true)]
        [string]$repoName,
        
        [Parameter(Mandatory=$true)]
        [string]$path,
        
        [Parameter(Mandatory=$true)]
        [string]$content
    )

    $base64Content = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($content));
    $sha = $(gh api "/repos/$repoName/contents/$path" --jq '.sha' 2>$null);
        
    $fields = @(
        "--field", "message=Update version to $versionParam",
        "--field", "content=$base64Content"
    );
        
    if ($sha -ne $null -and $sha -ne "") {
        $fields += "--field", "sha=$sha";
    }
        
	$result = gh api --method PUT "/repos/$repoName/contents/$path" @fields --silent 2>&1
	if ($LASTEXITCODE) { 
		throw "PushTextToRepo failed: $result"
	}
}