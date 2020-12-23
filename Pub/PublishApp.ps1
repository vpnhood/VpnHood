param(
    [Parameter(Mandatory=$true)] [String]$projectDir, 
    [Parameter(Mandatory=$true)] [String]$packagesDir,
    [Switch]$ftp, 
    [String]$packageName, 
    [String]$updateUrl=$null, 
    [String]$packageDownloadUrl=$null,
    [Switch]$withLauncher=$false,
    [Switch]$withVbsLauncher=$false)

. "$PSScriptRoot\Common.ps1"

# paths
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;
$assemblyName = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.AssemblyName;
$packageId = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.PackageId;
$packageId = "$packageId".Trim();
$publishDir = Join-Path $projectDir "bin\release\publish";
$publishPackDir = Join-Path $projectDir "bin\release\publish-pack";
if ($withVbsLauncher) {$withLauncher=$true}

#clean publish directory
Remove-Item "$publishDir\*" -ErrorAction Ignore -Recurse;

# Prepate AppHotUpdate
$outDir = $publishDir;
if ($withLauncher)
{
    $outDir = Join-Path $publishDir $versionParam;
    $publishInfoFile = Join-Path $publishDir "publish.json";
    $app_publishFile = Join-Path $publishDir "app_publish.txt";
    $launcherProjectDir = Join-Path $solutionDir "VpnHood.App.Launcher";

    # find launchFileName
    if ([string]::IsNullOrWhiteSpace($assemblyName))
    {
        Throw "Could not retrieve AssemblyName from the project!";
    }
    $assemblyName = ([string]$assemblyName).Trim();
    $launchFileName="$assemblyName.dll";

    # write publish info
    $json = @{
        Version=$versionParam; 
        LaunchPath=$versionParam + "/$launchFileName"; 
        UpdateUrl=$updateUrl;
        PackageDownloadUrl=$packageDownloadUrl;
        PackageFileName="$packageName.zip";
       };
    $json | ConvertTo-Json -depth 100 | Out-File $publishInfoFile;

    # Create launcher
    Write-Host;
    Write-Host "*** Creating Launcher..." -BackgroundColor Blue -ForegroundColor White;
    dotnet publish "$launcherProjectDir" -c "Release" --output "$publishDir\launcher" --framework net5.0 --no-self-contained /p:Version=$versionParam;
    if ($withVbsLauncher)
    {
        Copy-Item -path "$PSScriptRoot\run.vbs" -Destination "$publishDir\" -force
    }
}

if ($withLauncher)
{
    if ($credentials.$packageId)
    {
        $ftpAddress=$credentials.$packageId.FtpAddress;
        $ftpCredential=$credentials.$packageId.FtpCredential;
    }
}

# publish 
Write-Host;
Write-Host "*** Publishing $packageId..." -BackgroundColor Blue -ForegroundColor White;
if (-not $noclean)  { dotnet clean "$projectDir" -c "Release" --output $outDir  }
dotnet publish "$projectDir" -c "Release" --output $outDir --framework net5.0 --no-self-contained /p:Version=$versionParam
if ($LASTEXITCODE -gt 0) { Throw "The publish exited with error code: " + $lastexitcode; }

#####
# create zip package
$publishPackFileName = "$packageName.zip";
$publishPackFilePath = Join-Path $publishPackDir $publishPackFileName;
$publishPackInfoFilePath = Join-Path $publishPackDir "$packageName.json";

Write-Host;
Write-Host "*** Packing $publishPackFilePath..." -BackgroundColor Blue -ForegroundColor White;

New-Item -ItemType Directory -Force -Path $publishPackDir
Remove-Item "$publishPackDir\*" -ErrorAction Ignore -Recurse;
Compress-Archive -Path "$publishDir\*" -DestinationPath $publishPackFilePath;
$json | ConvertTo-Json -depth 100 | Out-File $publishPackInfoFilePath;

#####
# copy to solution output
New-Item -ItemType Directory -Path $packagesDir -Force 
Copy-Item -path "$publishPackDir\*" -Destination "$packagesDir\" -Force

#####
# upload publish folder
if ($ftpAddress -and $ftp)
{
    Write-Host "Uploading $publishPackFilePath";
    curl.exe "$ftpAddress/updates/$publishPackFileName" -u "$ftpCredential" -T $publishPackFilePath --ftp-create-dir --ssl;
    if ($LASTEXITCODE -gt 0) { Throw "curl exited with error code: " + $lastexitcode; }

    Write-Host "Uploading $publishPackInfoFilePath";
    curl.exe "$ftpAddress/updates/publish.json" -u "$ftpCredential" -T $publishPackInfoFilePath --ftp-create-dir --ssl;
    if ($LASTEXITCODE -gt 0) { Throw "curl exited with error code: " + $lastexitcode; }
}

# ReportVersion
ReportVersion