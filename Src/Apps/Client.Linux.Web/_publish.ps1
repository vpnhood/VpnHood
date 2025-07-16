# Calcualted Path
$repoBaseUrl= "https://github.com/vpnhood/VpnHood";

# Build arm64
$cpu = "arm64";
. "$PSScriptRoot/Pub/publish_impl_linux.ps1";
$installerUrl_arm64 = $module_installerUrl;

# Build x64
$cpu = "x64";
. "$PSScriptRoot/Pub/publish_impl_linux.ps1";
$installerUrl_x64 = $module_installerUrl;

# -----------------
# save the any file
# -----------------

# Prepare module folders
$moduleDir = "$packagesRootDir/$packageClientDirName/linux-any";
$moduleDirLatest = "$packagesRootDirLatest/$packageClientDirName/linux-any";
$module_installScriptFile = "$moduleDir/VpnHoodClient-linux.sh";
PrepareModuleFolder $moduleDir $moduleDirLatest;

# load install-any.sh file and replace installerUrls
$installAnyFile = "$PSScriptRoot/Pub/Linux/install-any.sh";
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




