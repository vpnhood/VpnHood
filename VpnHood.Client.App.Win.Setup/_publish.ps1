. "$PSScriptRoot\..\Pub\Common.ps1"
$advinstallerFile = (Get-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Caphyon\Advanced Installer" -Name "InstallRoot").InstallRoot;
$advinstallerFile = Join-Path $advinstallerFile "bin\x86\AdvancedInstaller.com";
$packageFile = Join-Path $PSScriptRoot "Release\VpnHoodClient-win.exe";
$updaterConfigFile= Join-Path $PSScriptRoot "Release\VpnHoodClient-win.txt";

Write-Host;
Write-Host "*** Creating ClientSetup..." -BackgroundColor Blue -ForegroundColor White;

$aipFile= Join-Path $PSScriptRoot "VpnHood.Client.App.Win.Setup.aip";

& $advinstallerFile /build $aipFile


# Create Updater Config File
$str=";aiu;

[Update]
Name = VpnHood
ProductVersion = $versionParam
URL = https://github.com/vpnhood/VpnHood/releases/download/$versionTag/VpnHoodClient-win.exe
Size = $((Get-Item $packageFile).length)
SHA256 = $((Get-FileHash $packageFile -Algorithm SHA256).Hash)
MD5 = $((Get-FileHash $packageFile -Algorithm MD5).Hash)
ServerFileName = VpnHoodClient-win.exe
Flags = NoRedetect
RegistryKey = HKUD\Software\VpnHood\VpnHood\Version
Version = $versionParam
UpdatedApplications = VpnHood(1.0-$versionParam)
Description = <a href=""https://github.com/vpnhood/VpnHood/blob/main/CHANGELOG.md"">Release note</a>
";
$str | Out-File -FilePath $updaterConfigFile


#####
# copy to solution output
New-Item -ItemType Directory -Path $packagesClientDir -Force 
Copy-Item -path $packageFile -Destination "$packagesClientDir\" -Force
Copy-Item -path $updaterConfigFile -Destination "$packagesClientDir\" -Force
