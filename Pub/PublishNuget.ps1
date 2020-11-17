param([Parameter(Mandatory=$true)] [String]$projectDir)

# paths
$solutionDir = Split-Path -parent $PSScriptRoot
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName
$assemblyName = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.AssemblyName
$packageId = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.PackageId
$publishDir = Join-Path $projectDir "bin\release\publish"

$credentials = (Get-Content "$solutionDir\..\.user\credentials.json" | Out-String | ConvertFrom-Json)
$versionBase = (Get-Content "$solutionDir\Publish\version.json" | Out-String | ConvertFrom-Json)
$versionBaseDate = [datetime]::new($versionBase.BaseYear, 1, 1)
$versionMajor = $versionBase.Major
$versionMinor = $versionBase.Minor

# find current version
$timeSpan = [datetime]::Now - $versionBaseDate
$version = [version]::new($versionMajor, $versionMinor, $timeSpan.Days, $timeSpan.Hours * 60 + $timeSpan.Minutes)
$versionParam = $version.ToString()

$apikey = $credentials.nugetApiKey

# Packing
Write-Host 
Write-Host "*** Packing..." -BackgroundColor Blue
rm "$publishDir" -ErrorAction Ignore -Recurse
dotnet pack "$projectDir" -c "Release" -o "$publishDir" --runtime any -p:Version=$versionParam -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg
if ($LASTEXITCODE -gt 0)
{
    Throw "The pack exited with error code: " + $lastexitcode
}

# publish nuget
Write-Host
Write-Host "*** Publishing..." -BackgroundColor Blue
$packageFile = (Join-Path $publishDir "$packageId.$versionParam.nupkg")
dotnet nuget push $packageFile --api-key $apikey --source https://api.nuget.org/v3/index.json
if ($LASTEXITCODE -gt 0)
{
    Throw "The publish exited with error code: " + $lastexitcode
}