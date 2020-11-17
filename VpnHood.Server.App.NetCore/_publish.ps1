# paths
$projectFile="VpnHood.Server.App.NetCore.csproj"
$launchFilePath="VpnHoodServer.dll"
$packageVersion = [Version] $xml.Project.PropertyGroup.Version
$projectDir = $PSScriptRoot
$credentials = (Get-Content "$projectDir\..\..\.user\credentials.json" | Out-String | ConvertFrom-Json)
$versionBase = (Get-Content "$projectDir\..\version.json" | Out-String | ConvertFrom-Json)

$projectDir=$PSScriptRoot
$publishDir="$projectDir\bin\release\publish"
$publishJsonFile = "$publishDir\publish.json"
$launcherProjectDir = "$projectDir\..\VpnHood.App.Launcher"
$ftpCredential=$credentials.ServerFtpCredential
$ftpAddress=$credentials.ServerFtpAddress
$versionBaseDate = [datetime]::new($versionBase.BaseYear, 1, 1)
$versionMajor = $versionBase.Major
$versionMinor = $versionBase.Minor

# find current version
$timeSpan = [datetime]::Now - $versionBaseDate
$version = [version]::new($versionMajor, $versionMinor, $timeSpan.Days, $timeSpan.Hours * 60 + $timeSpan.Minutes)
$version = [version]::Parse(([Xml] (Get-Content "$projectDir\$projectFile")).Project.PropertyGroup.Version)
$versionParam=$version.ToString(3)

# increase version and save
$json = @{Version=$versionParam; LaunchPath=$versionParam + "/$launchFilePath" }
$outDir = "$publishDir\" + $json.Version

# publish 
Write-Host 
Write-Host "*** Publishing..." -BackgroundColor Blue
#dotnet publish "$projectDir" -c "Release" --output "$outDir" --framework net5.0 --no-self-contained /p:Version=$versionParam
dotnet publish "$projectDir" -c "Release" --output "$outDir" --framework net5.0 --no-self-contained /p:Version=$versionParam
$json | ConvertTo-Json -depth 100 | Out-File $publishJsonFile
if ($LASTEXITCODE -gt 0)
{
    Throw "The publish exited with error code: " + $lastexitcode
}

# Create launcher
Write-Host 
Write-Host "*** Creating Launcher..." -BackgroundColor Blue
dotnet publish "$launcherProjectDir" -c "Release" --output "$publishDir" --framework net5.0 --no-self-contained /p:Version=$versionParam

# upload publish folder
Write-Host 
Write-Host "*** Uploading..." -BackgroundColor Blue
$files = 
    (get-childitem $outDir -recurse -File -exclude ("appsettings.json", "*.pfx")).FullName + 
    (get-childitem $publishDir -File -filter "run*").FullName +
    $publishJsonFile

foreach ($file in $files)
{
    $fullName = $file
    $fileR=$fullName.Substring($publishDir.Length + 1).Replace("\", "/")
    Write-Host "Uploading $fileR"
    curl.exe "$ftpAddress/$fileR" -u "$ftpCredential" -T "$fullName" --ftp-create-dir --ssl
    if ($LASTEXITCODE -gt 0)
    {
        Throw "curl exited with error code: " + $lastexitcode
    }
}
