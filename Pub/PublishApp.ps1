param([Parameter(Mandatory=$true)] [String]$projectDir)

# paths
$solutionDir = Split-Path -parent $PSScriptRoot
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName
$assemblyName = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.AssemblyName
$packageId = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.PackageId
$packageId = "$packageId".Trim()
$publishDir = Join-Path $projectDir "bin\release\publish"

$credentials = (Get-Content "$solutionDir\..\.user\credentials.json" | Out-String | ConvertFrom-Json)
$versionBase = (Get-Content "$solutionDir\Pub\version.json" | Out-String | ConvertFrom-Json)
$versionBaseDate = [datetime]::new($versionBase.BaseYear, 1, 1)
$versionMajor = $versionBase.Major
$versionMinor = $versionBase.Minor

# find current version
$timeSpan = [datetime]::Now - $versionBaseDate
$version = [version]::new($versionMajor, $versionMinor, $timeSpan.Days, $timeSpan.Hours * 60 + $timeSpan.Minutes)
$versionParam = $version.ToString()

# Prepate AppHotUpdate
$outDir = Join-Path $publishDir $versionParam
$publishInfoFile = Join-Path $publishDir publish.json
$launchFilePath="$assemblyName.dll"
$launcherProjectDir = Join-Path $solutionDir "VpnHood.App.Launcher"
if ($credentials.$packageId)
{
    $ftpAddress=$credentials.$packageId.FtpAddress
    $ftpCredential=$credentials.$packageId.FtpCredential
}

# publish 
Write-Host 
Write-Host "*** Publishing $packageId..." -BackgroundColor Blue
Remove-Item "$publishDir\*" -ErrorAction Ignore -Recurse
dotnet publish "$projectDir" -c "Release" --output $outDir --framework net5.0 --no-self-contained /p:Version=$versionParam

$json = @{Version=$versionParam; LaunchPath=$versionParam + "/$launchFilePath" }
$json | ConvertTo-Json -depth 100 | Out-File $publishInfoFile
if ($LASTEXITCODE -gt 0)
{
    Throw "The publish exited with error code: " + $lastexitcode
}

# Create launcher
Write-Host 
Write-Host "*** Creating Launcher..." -BackgroundColor Blue
dotnet publish "$launcherProjectDir" -c "Release" --output "$publishDir" --framework net5.0 --no-self-contained /p:Version=$versionParam

# upload publish folder
if ($ftpAddress)
{
    Write-Host 
    Write-Host "*** Uploading..." -BackgroundColor Blue
    $files = (get-childitem $publishDir -recurse -File -exclude ("appsettings.json", "*.pfx")).FullName 

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
}
