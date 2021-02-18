param([switch]$bump)

$solutionDir = Split-Path -parent $PSScriptRoot;
$msbuild = Join-Path ${Env:ProgramFiles(x86)} "Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
$credentials = (Get-Content "$solutionDir/../.user/credentials.json" | Out-String | ConvertFrom-Json);
$nugetApiKey = $credentials.NugetApiKey;
$nuget = Join-Path $PSScriptRoot "nuget.exe";
$packagesRootDir = "$PSScriptRoot/bin";
$packagesClientDir="$packagesRootDir/Client";
$packagesServerDir="$packagesRootDir/Server";
$env:GITHUB_TOKEN = $credentials.GithubToken;

# version
$versionFile = Join-Path $PSScriptRoot "version.json"
$versionJson = (Get-Content $versionFile | Out-String | ConvertFrom-Json);
$bumpTime = [datetime]::Parse($versionJson.BumpTime);
$autoBump=((Get-Date)-$bumpTime).TotalMinutes -ge 30;
if ( $bump )
{
	$isVersionBumped = $true;
	$versionJson.Build = $versionJson.Build + 1;
	$versionJson.BumpTime = [datetime]::UtcNow.ToString("o");
	$versionJson | ConvertTo-Json -depth 10 | Out-File $versionFile;
}
$version=[version]::new($versionJson.Major, $versionJson.Minor, $versionJson.Build, 0);
$versionParam = $version.ToString(3);
$versionTag="v$versionParam-beta";

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
