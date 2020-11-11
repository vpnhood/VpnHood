# paths
$projectDir=$PSScriptRoot
$publishDir="$projectDir\bin\release\publish"
$publishJsonFile = "$publishDir\publish.json"
$launchFilePath="VpnHood.AccessServer.exe"
$versionBaseDate = [datetime]::new(2020, 1, 1)
$versionMajor = 1;
$versionMinor = 0;

# find current version
$timeSpan = [datetime]::Now - $versionBaseDate
$version = [version]::new($versionMajor, $versionMinor, $timeSpan.Days, $timeSpan.Hours * 60 + $timeSpan.Minutes)

# increase version and save
$json = @{version=$version.ToString(4); launchPath=$version.ToString(4) + "/$launchFilePath" }
$outDir = "$publishDir\" + $json.version
echo $json.Version

# publish 
# Remove-Item -Path "$publishdir" -Recurse
$versionParam=$version.ToString(4)
dotnet publish "$projectDir" -c "Release" --output "$outDir" --framework netcoreapp3.1 --runtime win-x64 --no-self-contained /p:Version=$versionParam

$json | ConvertTo-Json -depth 100 | Out-File $publishJsonFile
