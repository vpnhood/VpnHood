. "$PSScriptRoot\..\Pub\Common.ps1";
$projectDir = $PSScriptRoot;
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;

Write-Host;
Write-Host "*** Creating Windows Server Module..." -BackgroundColor Blue -ForegroundColor White;

# prepare module folders
$moduleDir = "$packagesServerDir/win-x64";
$moduleDirLatest = "$packagesServerDirLatest/win-x64";
PrepareModuleFolder $moduleDir $moduleDirLatest;

# Creating linux package
$templateDir = "$PSScriptRoot/Install/Win";
$template_installScriptFile = "$templateDir/install.ps1";
$template_launcherFile = "$templateDir/vhserver.ps1";
$template_updaterFile = "$templateDir/updater.ps1";

$publishDir = "$projectDir/bin/release/publish-win";
$publish_infoDir = "$publishDir/$versionTag/publish_info";
$publish_updaterFile = "$publish_infoDir/vhupdate.ps1";
$publish_launcherFile = "$publish_infoDir/vhserver.ps1";
$publish_InfoFile = "$publish_infoDir/publish.json";

$module_InfoFile = "$moduleDir/VpnHoodServer-win.json";
$module_InstallerFile = "$moduleDir/VpnHoodServer-win.ps1";
$module_PackageFile = "$moduleDir/VpnHoodServer-win.zip";

$lineEnding = "`r`n";
$runtime = "win-x64";
$launcher_exeFile = "$versionTag/VpnHoodServer.exe";
. "$PSScriptRoot\_publish_impl.ps1";
