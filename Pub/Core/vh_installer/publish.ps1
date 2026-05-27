param(
    [Parameter(Mandatory = $true)] [string]$projectDir,
    [Parameter(Mandatory = $true)] [string]$repoBaseUrl,
    [Parameter(Mandatory = $true)] [string]$publishDirName,
    [Parameter(Mandatory = $true)] [string]$launcherName,
    [Parameter(Mandatory = $true)] [string]$os
)

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
$installAnyFile = "$PSScriptRoot/$os/install-any.$shellExt";
$installAnyContent = Get-Content -Path $installAnyFile -Raw;
$installAnyContent = $installAnyContent.Replace('$installerUrl_arm64', $installerUrl_arm64);
$installAnyContent = $installAnyContent.Replace('$installerUrl_x64', $installerUrl_x64);
$installAnyContent = $installAnyContent -replace "`r`n", $lineEnding;
$installAnyContent | Out-File -FilePath $module_installScriptFile -Encoding ASCII -Force -NoNewline;

# copy install-msquic.sh to VpnHoodServer-linux-msquic.sh and remove \r
Write-Host "Copying msquic installer..." -ForegroundColor Green
$msquicSrc = "$solutionDir/Src/Apps/Server.Net/Pub/Linux/install-msquic.sh";
$msquicDst = "$moduleDirLatest/VpnHoodServer-linux-msquic.sh";
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
