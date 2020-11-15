# paths
$projectDir = $PSScriptRoot
$credentials = (Get-Content "$projectDir\..\..\.user\credentials.json" | Out-String | ConvertFrom-Json)
$versionBase = (Get-Content "$projectDir\..\version.json" | Out-String | ConvertFrom-Json)

$apikey = $credentials.nugetApiKey
$packageName = "VpnHood.Server.Common"
$publishDir = "$projectDir\bin\release\publish"
$versionBaseDate = [datetime]::new($versionBase.BaseYear, 1, 1)
$versionMajor = $versionBase.Major;
$versionMinor = $versionBase.Minor;

# find current version
$timeSpan = [datetime]::Now - $versionBaseDate
$version = [version]::new($versionMajor, $versionMinor, $timeSpan.Days, $timeSpan.Hours * 60 + $timeSpan.Minutes)


# Packing
Write-Host 
Write-Host "*** Packing..." -BackgroundColor Blue
$versionParam=$version.ToString(4)
Remove-Item –path "$publishDir" –recurse
dotnet pack "$projectDir" -c "Release" -o "$publishDir" --runtime any -p:Version=$versionParam -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg
if ($LASTEXITCODE -gt 0)
{
    Throw "The pack exited with error code: " + $lastexitcode
}

# publish nuget
Write-Host
Write-Host "*** Publishing..." -BackgroundColor Blue
dotnet nuget push "$publishDir\$packageName.$versionParam.nupkg" --api-key $apikey --source https://api.nuget.org/v3/index.json