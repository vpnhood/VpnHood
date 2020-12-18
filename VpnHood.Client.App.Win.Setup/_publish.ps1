. "$PSScriptRoot\..\Pub\Common.ps1"
$advinstallerFile = (Get-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Caphyon\Advanced Installer" -Name "InstallRoot").InstallRoot;
$advinstallerFile = Join-Path $advinstallerFile "bin\x86\AdvancedInstaller.com";
$packageFile = Join-Path $PSScriptRoot "Release\VpnHoodClient-win.exe";

Write-Host;
Write-Host "*** Creating ClientSetup..." -BackgroundColor Blue -ForegroundColor White;

$aipFile= Join-Path $PSScriptRoot "VpnHood.Client.App.Win.Setup.aip";

& $advinstallerFile /build $aipFile

#####
# copy to solution output
Copy-Item -path $packageFile -Destination "$packagesDir\" -force
