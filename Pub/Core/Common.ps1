param(
	[int]$bump
)
$ErrorActionPreference = "Stop";

$solutionDir = Split-Path -parent (Split-Path -parent $PSScriptRoot);
$vhDir = Split-Path -parent $solutionDir;
$pubDir = "$solutionDir/Pub";
$msbuild = Join-Path ${Env:Programfiles} "Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe";
$userDir = "$solutionDir/../.user";
$credentials = (Get-Content "$userDir/credentials.json" | Out-String | ConvertFrom-Json);
$nugetApiKey = $credentials.NugetApiKey;
$msverbosity = "minimal";

# Version
. "$PSScriptRoot/VersionBump.ps1" -versionFile "$pubDir/PubVersion.json" -bump $bump;

# Packages Directory
$packagesRootDir = "$pubDir/bin/" + $versionTag;
$packageServerDirName = "VpnHoodServer";
$packageClientDirName = "VpnHoodClient";
$packageConnectDirName = "VpnHoodConnect";

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