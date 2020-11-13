# paths
$projectDir=$PSScriptRoot
$publishDir="$projectDir\bin\release\publish"
$publishJsonFile = "$publishDir\publish.json"
$launchFilePath="VpnHood.AccessServer.exe"
$ftpCredential=(Get-Content "$projectDir\_publish.credential" | Out-String | ConvertFrom-Json).FtpCredential
$ftpAddress=(Get-Content "$projectDir\_publish.credential" | Out-String | ConvertFrom-Json).FtpAddress
$versionBaseDate = [datetime]::new(2020, 1, 1)
$versionMajor = 1;
$versionMinor = 0;

# find current version
$timeSpan = [datetime]::Now - $versionBaseDate
$version = [version]::new($versionMajor, $versionMinor, $timeSpan.Days, $timeSpan.Hours * 60 + $timeSpan.Minutes)

# increase version and save
$json = @{Version=$version.ToString(4); LaunchPath=$Version.ToString(4) + "/$launchFilePath" }
$outDir = "$publishDir\" + $json.Version

# publish 
$versionParam=$version.ToString(4)
dotnet publish "$projectDir" -c "Release" --output "$outDir" --framework netcoreapp3.1 --runtime win-x64 --no-self-contained /p:Version=$versionParam
$json | ConvertTo-Json -depth 100 | Out-File $publishJsonFile
if ($LASTEXITCODE -gt 0)
{
    Throw "The publish exited with error code: " + $lastexitcode
}

# upload publish folder
$files = get-childitem $outDir -recurse -File
foreach ($file in $files)
{
    $fullName = $file.FullName
    $fileR=$file.FullName.Substring($outDir.Length + 1).Replace("\", "/")
    Write-Host "Uploading $fileR"
    curl.exe "$ftpAddress/$versionParam/$fileR" -u "$ftpCredential" -T "$fullName" --ftp-create-dir --ssl
    if ($LASTEXITCODE -gt 0)
    {
        Throw "curl exited with error code: " + $lastexitcode
    }
}

# upload publish json
curl.exe "$ftpAddress/publish.json" -u "$ftpCredential" -T "$publishJsonFile" --ssl
