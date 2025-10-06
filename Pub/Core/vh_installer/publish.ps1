param(
    [Parameter(Mandatory = $true)] [string]$projectDir,
    [Parameter(Mandatory = $true)] [string]$repoBaseUrl,
    [Parameter(Mandatory = $true)] [string]$publishDirName,
    [Parameter(Mandatory = $true)] [string]$launcherName,
    [Parameter(Mandatory = $true)] [string]$os,
    [Parameter(Mandatory = $true)] [boolean]$autoLaunch
)

# Build x64
. "$PSScriptRoot/publish_impl.ps1" `
    -projectDir $projectDir -repoBaseUrl $repoBaseUrl -os $os `
    -publishDirName $publishDirName -launcherName $launcherName -autoLaunch $autoLaunch `
    -cpu "x64";

$installerUrl_x64 = $module_installerUrl;

# Build arm64
. "$PSScriptRoot/publish_impl.ps1" `
    -projectDir $projectDir -repoBaseUrl $repoBaseUrl -os $os `
    -publishDirName $publishDirName -launcherName $launcherName -autoLaunch $autoLaunch `
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

# Copy the installer script to latest
if ($isLatest)
{
    Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse
}
