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
Write-Host "*** $packageId > Packing..." -BackgroundColor Blue
rm "$publishDir" -ErrorAction Ignore -Recurse
$nugetVersion="$versionParam" + (&{if($prerelease) {"-prerelease"} else {""}});
dotnet pack "$projectDir" -c "Release" -o "$publishDir" --runtime any -p:Version=$nugetVersion -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg
if ($LASTEXITCODE -gt 0) { Throw "The pack exited with error code: " + $lastexitcode; }

# publish nuget
if (!$noPushNuget)
{
	Write-Host
	Write-Host "*** $packageId > Publishing..." -BackgroundColor Blue
	$packageFile = (Join-Path $publishDir "$packageId.$nugetVersion.nupkg")
	dotnet nuget push $packageFile --api-key $nugetApiKey --source https://api.nuget.org/v3/index.json
	if ($LASTEXITCODE -gt 0) { Write-Host ("The publish exited with error code: " + $lastexitcode) -ForegroundColor Red;  }
}

# ReportVersion
ReportVersion