param(
    [Parameter(Mandatory=$true)] [String]$projectDir, 
    [Switch]$ftp, 
    [String]$packageName, 
    [String]$packagesDir=$null,
    [String]$updateUrl=$null, 
    [String]$packageDownloadUrl=$null,
    [Switch]$withLauncher=$false)

# Info
Write-Host;
Write-Host "*** Building $packageName..." -BackgroundColor Yellow -ForegroundColor Black;

# Common
. "$PSScriptRoot\Common.ps1"

# paths
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;
$assemblyName = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.AssemblyName;
$targetFramework = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.TargetFramework;

$packageId = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.PackageId;
$packageId = "$packageId".Trim();
$publishDir = Join-Path $projectDir "bin/release/publish";
$publishPackDir = Join-Path $projectDir "bin/release/publish-pack";

#clean publish directory
$_ = New-Item -ItemType Directory -Force -Path $publishDir;
Remove-Item "$publishDir/*" -ErrorAction Ignore -Recurse;

#update project version
UpdateProjectVersion $projectFile;

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
        TargetFramework="$targetFramework".Trim();
        UpdateUrl=$updateUrl;
        PackageDownloadUrl=$packageDownloadUrl;
        PackageFileName="$packageName.zip";
       };
    $json | ConvertTo-Json -depth 100 | Out-File $publishInfoFile;

    # Create launcher
    Write-Host;
    Write-Host "*** Creating Launcher..." -BackgroundColor Blue -ForegroundColor White;
    UpdateProjectVersion (Join-Path $launcherProjectDir "VpnHood.App.Launcher.csproj");
    dotnet publish "$launcherProjectDir" -c "Release" --output "$publishDir\launcher" --framework net5.0 --no-self-contained /p:Version=$versionParam;
    if ($withVbsLauncher)
    {
        Copy-Item -path "$PSScriptRoot\run.vbs" -Destination "$publishDir\" -force
    }
}

# publish 
Write-Host;
Write-Host "*** Publishing $packageId..." -BackgroundColor Blue -ForegroundColor White;
if (-not $noclean)  { dotnet clean "$projectDir" -c "Release" --output $outDir  }
dotnet publish "$projectDir" -c "Release" --output $outDir --framework $targetFramework --no-self-contained /p:Version=$versionParam
if ($LASTEXITCODE -gt 0) { Throw "The publish exited with error code: " + $lastexitcode; }

# create zip package and zip updater
if ($withLauncher)
{
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
    if ($packagesDir)
    {
        New-Item -ItemType Directory -Path $packagesDir -Force 
        Copy-Item -path "$publishPackDir\*" -Destination "$packagesDir\" -Force
    }
}


# ReportVersion
ReportVersion