param(
	[int]$bump
)
$ErrorActionPreference = "Stop";

$solutionDir = Split-Path -parent $PSScriptRoot;
$msbuild = Join-Path ${Env:Programfiles} "Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
$credentials = (Get-Content "$solutionDir/../.user/credentials.json" | Out-String | ConvertFrom-Json);
$nugetApiKey = $credentials.NugetApiKey;
$nuget = Join-Path $PSScriptRoot "nuget.exe";
$msverbosity = "minimal";

# Version
$versionFile = Join-Path $PSScriptRoot "version.json"
$versionJson = (Get-Content $versionFile | Out-String | ConvertFrom-Json);
$bumpTime = [datetime]::Parse($versionJson.BumpTime);
if ( $bump -gt 0 )
{
	$isVersionBumped = $true;
	$versionJson.Build = $versionJson.Build + 1;
	$versionJson.BumpTime = [datetime]::UtcNow.ToString("o");
	$versionJson.Prerelease = ($bump -eq 2);
	$versionJson | ConvertTo-Json -depth 10 | Out-File $versionFile;
}

$prerelease=$versionJson.Prerelease;
$isLatest=$versionJson.Prerelease -eq $false; 
$version=[version]::new($versionJson.Major, $versionJson.Minor, $versionJson.Build, 0);
$versionParam = $version.ToString(3);
$versionTag="v$versionParam" + (&{if($prerelease) {"-prerelease"} else {""}});

# Packages Directory
$packagesRootDir = "$PSScriptRoot/bin/" + $versionTag;
$packagesClientDir="$packagesRootDir/Client";
$packagesServerDir="$packagesRootDir/Server";
New-Item -ItemType Directory -Path $packagesClientDir -Force | Out-Null
New-Item -ItemType Directory -Path $packagesServerDir -Force | Out-Null


# Prepare the latest folder
$packagesRootDirLatest = "$PSScriptRoot/bin/latest" + (&{if($isLatest) {""} else {"/????"}});
$packagesClientDirLatest="$packagesRootDirLatest/Client";
$packagesServerDirLatest="$packagesRootDirLatest/Server";
if ($isLatest)
{
	New-Item -ItemType Directory -Path $packagesClientDirLatest -Force | Out-Null
	New-Item -ItemType Directory -Path $packagesServerDirLatest -Force | Out-Null
}

# UpdateProjectVersion
Function UpdateProjectVersion([string] $projectFile) 
{
	$xml = New-Object XML;
	$xml.PreserveWhitespace = $true;
	$xml.Load($projectFile);
	$assemblyVersion = $xml.SelectSingleNode("Project/PropertyGroup/AssemblyVersion");
	$fileVersion = $xml.SelectSingleNode("Project/PropertyGroup/FileVersion");
	$packageVersion = $xml.SelectSingleNode("Project/PropertyGroup/Version");
	if ($assemblyVersion -and $assemblyVersion.InnerText -ne $versionParam){
		$assemblyVersion.InnerText = $versionParam;
		$fileVersion.InnerText = $versionParam;
		$packageVersion.InnerText = $versionParam;
		$xml.Save($projectFile);
	}
}

# ReportVersion
Function ReportVersion() 
{
	Write-Host "version: $versionParam" -ForegroundColor GREEN;
}

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
