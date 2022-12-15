. "$PSScriptRoot\..\Pub\Common.ps1"

# prepare module folders
Write-Host;
Write-Host "*** Building Client Windows Setup..." -BackgroundColor Blue -ForegroundColor White;
$moduleDir = "$packagesClientDir/windows";
$moduleDirLatest = "$packagesClientDirLatest/windows";
PrepareModuleFolder $moduleDir $moduleDirLatest;

$advinstallerFile = (Get-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Caphyon\Advanced Installer" -Name "InstallRoot").InstallRoot;
$advinstallerFile = Join-Path $advinstallerFile "bin\x86\AdvancedInstaller.com";

$module_title = "VpnHoodClient-win-x64";
$module_packageFileName = "$module_title.exe"
$module_packageFile = Join-Path $PSScriptRoot "Release\$module_packageFileName";
$module_updaterConfigFile= Join-Path $PSScriptRoot "Release\$module_title.txt";

$aipFile= Join-Path $PSScriptRoot "VpnHood.Client.App.Win.Setup.aip";
& $advinstallerFile /build $aipFile

# Create Updater Config File
$str=";aiu;

[Update]
Name = VpnHood $versionParam
ProductVersion = $versionParam
URL = https://github.com/vpnhood/VpnHood/releases/download/$versionTag/$module_packageFileName
Size = $((Get-Item $module_packageFile).length)
SHA256 = $((Get-FileHash $module_packageFile -Algorithm SHA256).Hash)
MD5 = $((Get-FileHash $module_packageFile -Algorithm MD5).Hash)
ServerFileName = $moduleFileName.exe
Flags = NoRedetect
RegistryKey = HKUD\Software\VpnHood\VpnHood\Version
Version = $versionParam
UpdatedApplications = VpnHood(1.0-$versionParam)
Description = <a href=""https://github.com/vpnhood/VpnHood/blob/main/CHANGELOG.md"">Release note</a>
";
$str | Out-File -FilePath $module_updaterConfigFile


#####
# copy to module
Copy-Item -path "$module_packageFile" -Destination "$moduleDir/" -Force
Copy-Item -path "$module_updaterConfigFile" -Destination "$moduleDir/" -Force

if ($isLatest)
{
	Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse
	Copy-Item -path "$module_updaterConfigFile" -Destination "$moduleDirLatest/VpnHoodClient-win.txt" -Force -Recurse # legacy
}
