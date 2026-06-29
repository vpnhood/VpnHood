param(
	[int]$bump
)
$ErrorActionPreference = "Stop";

$solutionDir = Split-Path -parent (Split-Path -parent $PSScriptRoot);
$gitDir = "$solutionDir/.git";
$vhDir = Split-Path -parent $solutionDir;
$pubDir = "$solutionDir/Pub";
if ($env:ProgramFiles) {
	$msbuild = Join-Path ${Env:Programfiles} "Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe";
}
$userDir = "$solutionDir/../.user";
# Secrets live as discrete files under .user/ (one value per file) so they map 1:1 to GitHub
# secrets. Android keystores/passwords resolve per-app in PublishAndroidApp.ps1; here we only
# need the global NuGet token. Missing file -> empty (a fork without secrets still builds).
$nugetApiKey = if (Test-Path "$userDir/nuget_apikey.txt") { (Get-Content "$userDir/nuget_apikey.txt" -Raw).Trim() } else { "" };
$msverbosity = "minimal";

# Version
. "$PSScriptRoot/VersionBump.ps1" -versionFile "$pubDir/PubVersion.json" -bump $bump;

# Packages Directory
$packagesRootDir = "$pubDir/bin/" + $versionTag;
$packageServerDirName = "VpnHoodServer";
$packageClientDirName = "VpnHoodClient";
$packageConnectDirName = "VpnHoodConnect";

# Resolve the publish target repo(s). Defaults to the CURRENT repo so a fork publishes to itself;
# override with VH_PUBLISH_REPO (client) / VH_CONNECT_PUBLISH_REPO (connect). See ResolvePublishRepo.ps1.
# AppPublishConfig provides Get-AppPublishConfig for per-app .user/<packageFileTitle>/ config files.
. "$PSScriptRoot/ResolvePublishRepo.ps1";
. "$PSScriptRoot/AppPublishConfig.ps1";
$publishRepo = Resolve-PublishRepoSlug;
$publishRepoUrl = Resolve-PublishRepoUrl;
$connectPublishRepo = Resolve-PublishRepoSlug -Connect;
$connectPublishRepoUrl = Resolve-PublishRepoUrl -Connect;

# Prepare the latest folder
$packagesRootDirLatest = "$pubDir/bin/latest";

# Release root such as latet or pre-release folder
$releaseRootDir = (&{if($isLatest) {$packagesRootDirLatest} else {$packagesRootDir}})

# ZipFiles, PowerShell Compression has a bug and does not respoect slash for linux
function ZipFiles([string]$Path, [string]$DestinationPath)
{
	# PowerShell Compress-Archive is not compatible on linux
	# Compress-Archive -Path "$distDir\*" -DestinationPath $dest1 -Force; 
	tar.exe -C "$Path" -a -cf "$DestinationPath" "*"
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

function UpdateRepoVersionInFile()
{
	$files = Get-ChildItem -Path @($packagesRootDirLatest, $moduleGooglePlayLastestDir) `
			-File -Recurse | Where-Object { $_.Extension -eq '.json' -or $_.Extension -eq '.txt' -or $_.Extension -eq '.sh'  }
	
	# Loop through each file and apply the change
	foreach ($file in $files) 
	{
		$fileContent = Get-Content $file.FullName -Raw;
		$fileContent = $fileContent -replace "/download/v(\d+\.\d+\.\d+)/", "/download/$versionTag/";
		Set-Content -Path $file.FullName -Value $fileContent -Encoding ASCII -Force -NoNewline;
	}	
}

function Get-RolloutPercentage {
	param(
		[Parameter(Mandatory=$true)][bool]$distribute,
		[Parameter(Mandatory=$true)][int]$rollout
	)
	
	if ($distribute -and ($rollout -le 0 -or $rollout -gt 100)) {
		[int]$parsedRollout = 0
		$rolloutInput = Read-Host "Enter rollout percentage (1-100, default 100)";
		if ([string]::IsNullOrWhiteSpace($rolloutInput)) {
			return 100;
		} elseif ([int]::TryParse($rolloutInput, [ref]$parsedRollout) -and $parsedRollout -ge 1 -and $parsedRollout -le 100) {
			return $parsedRollout;
		} else {
			throw "Invalid rollout.";
		}
	}
	
	return $rollout;
}

function CommitAndPushToMainRepo {
	Write-Host "Commit & push current changes to the main repo"
	git --git-dir=$gitDir --work-tree=$solutionDir add -A
	git --git-dir=$gitDir --work-tree=$solutionDir commit -m "Publish $versionTag"

	Write-Host "Push to main"
	git --git-dir=$gitDir --work-tree=$solutionDir push origin development:main --force 
}

function CommitAndSyncMainRepo {
	Write-Host "Commit & push current changes to the main repo"
	git --git-dir=$gitDir --work-tree=$solutionDir add -A
	git --git-dir=$gitDir --work-tree=$solutionDir commit -m "Publish $versionTag"
	git --git-dir=$gitDir --work-tree=$solutionDir pull
	git --git-dir=$gitDir --work-tree=$solutionDir push
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