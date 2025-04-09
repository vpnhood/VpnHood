param([Parameter(Mandatory=$true)] [String]$projectDir)
. "$PSScriptRoot/Common.ps1"

# paths
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;
$assemblyName = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.AssemblyName;
if ($assemblyName -eq $null) {$assemblyName = (Get-Item $projectFile).BaseName};
$packageId = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.PackageId;
if ($packageId -eq $null) {$packageId = $assemblyName};
$packageId = "$packageId".Trim();
$publishDir = Join-Path $projectDir "bin\release\publish";

#update project version
UpdateProjectVersion $projectFile;

# packing
Write-Host 
Write-Host "*** $packageId > Nuget..." -BackgroundColor Blue
rm "$publishDir" -ErrorAction Ignore -Recurse
$nugetVersion="$versionParam" + (&{if($prerelease) {"-prerelease"} else {""}});
dotnet pack "$projectDir" -c "Release" -o "$publishDir" -p:Version=$nugetVersion `
	-p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg -p:SolutionDir=$solutionDir;

if ($LASTEXITCODE -gt 0) { Throw "The pack exited with error code: " + $lastexitcode; }

# publish nuget
if (!$noPushNuget)
{
	Write-Host
	Write-Host "*** $packageId > Publishing..."
	$packageFile = (Join-Path $publishDir "$packageId.$nugetVersion.nupkg")
	dotnet nuget push $packageFile --source "https://api.nuget.org/v3/index.json" --api-key $nugetApiKey
	if ($LASTEXITCODE -gt 0) { Write-Host ("The publish exited with error code: " + $lastexitcode) -ForegroundColor Red;  }
}

# ReportVersion
ReportVersion