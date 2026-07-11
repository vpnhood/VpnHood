param(
    [Parameter(Mandatory = $true)] [string]$projectDir,
    # Optional: an explicit release base URL (Server passes one). When omitted it is taken from the
    # per-app .user override or resolved from the current repo (see below).
    [Parameter(Mandatory = $false)] [string]$repoBaseUrl,
    [Parameter(Mandatory = $true)] [string]$publishDirName,
    [Parameter(Mandatory = $true)] [string]$launcherName,
    [Parameter(Mandatory = $true)] [string]$os,
    # Release repo for Connect (VH_CONNECT_PUBLISH_REPO) vs client.
    [switch]$connect
)

# Per-app config + repo resolution. publish_impl.ps1 sources Common.ps1, but we need these before the
# first call to pass a resolved -repoBaseUrl, so dot-source the (side-effect-free) helpers here. The
# config lives at .user/<publishDirName>/publish.json (publishDirName is the app's packageFileTitle).
# Linux has no packageId, so only repoUrl is read here.
. "$PSScriptRoot/../ResolvePublishRepo.ps1";
. "$PSScriptRoot/../AppPublishConfig.ps1";
$appConfig = Get-AppPublishConfig $publishDirName;
$repoBaseUrl =
    if ($appConfig.repoUrl) { $appConfig.repoUrl }
    elseif (-not [string]::IsNullOrWhiteSpace($repoBaseUrl)) { $repoBaseUrl }   # explicit (e.g. Server)
    else { Resolve-PublishRepoUrl -Connect:$connect };
# Strict: the app's shared appsettings (embedded as AppSettings.json) must exist when strict.
# Server has no publish.json, so Assert-AppSettings short-circuits (exists=false) and never throws.
Assert-AppSettings $publishDirName;

# Build x64
. "$PSScriptRoot/publish_impl.ps1" `
    -projectDir $projectDir -repoBaseUrl $repoBaseUrl -os $os `
    -publishDirName $publishDirName -launcherName $launcherName `
    -cpu "x64";

$installerUrl_x64 = $module_installerUrl;

# Build arm64
. "$PSScriptRoot/publish_impl.ps1" `
    -projectDir $projectDir -repoBaseUrl $repoBaseUrl -os $os `
    -publishDirName $publishDirName -launcherName $launcherName `
    -cpu "arm64";

$installerUrl_arm64 = $module_installerUrl;


# -----------------
# save the any file
# -----------------

# Prepare module folders
$moduleDir = "$packagesRootDir/$publishDirName/$os-any";
$moduleDirLatest = "$packagesRootDirLatest/$publishDirName/$os-any";
$module_installScriptFile = "$moduleDir/$assemblyName-$os.$shellExt";
PrepareModuleFolder $moduleDir $moduleDirLatest;

# load install-any shell file and replace installerUrls
Write-Host "Preparing installer script for any CPU..." -ForegroundColor Green
$installAnyFile = "$PSScriptRoot/$os/install-any.$shellExt";
$installAnyContent = Get-Content -Path $installAnyFile -Raw;
$installAnyContent = $installAnyContent.Replace('$installerUrl_arm64', $installerUrl_arm64);
$installAnyContent = $installAnyContent.Replace('$installerUrl_x64', $installerUrl_x64);
$installAnyContent = $installAnyContent -replace "`r`n", $lineEnding;
$installAnyContent | Out-File -FilePath $module_installScriptFile -Encoding ASCII -Force -NoNewline;

# copy install-msquic.sh to VpnHoodServer-linux-msquic.sh and remove \r
Write-Host "Copying msquic installer..." -ForegroundColor Green
$msquicSrc = "$solutionDir/src/Apps/Server.Net/pub/Linux/install-msquic.sh";
$msquicDst = "$moduleDir/VpnHoodServer-linux-msquic.sh";
New-Item -ItemType Directory -Path (Split-Path $msquicDst -Parent) -Force | Out-Null
(Get-Content $msquicSrc -Raw) -replace "`r", "" | Out-File -FilePath $msquicDst -Encoding ASCII -Force -NoNewline
if ($isLatest)
{
    Copy-Item -path "$msquicSrc" -Destination "$moduleDirLatest/VpnHoodServer-linux-msquic.sh" -Force -Recurse
}

# Copy the installer script to latest
if ($isLatest)
{
    Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse
}
