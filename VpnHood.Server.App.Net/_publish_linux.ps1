. "$PSScriptRoot\..\Pub\Common.ps1";
$projectDir = $PSScriptRoot;
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;

Write-Host;
Write-Host "*** Creating Linux Server Module..." -BackgroundColor Blue -ForegroundColor White;

# prepare module folders
$moduleDir = "$packagesServerDir/linux-x64";
$moduleDirLatest = "$packagesServerDirLatest/linux-x64";
PrepareModuleFolder $moduleDir $moduleDirLatest;

# Creating linux package
$templateDir = "$PSScriptRoot/Install/Linux";
$template_installScriptFile = "$templateDir/install.sh";
$template_launcherFile = "$templateDir/vhserver.sh";
$template_updaterFile = "$templateDir/updater.sh";

$publishDir = "$projectDir/bin/release/publish-linux";
$publish_infoDir = "$publishDir/$versionTag/publish_info";
$publish_updaterFile = "$publish_infoDir/vhupdate";
$publish_launcherFile = "$publish_infoDir/vhserver";
$publish_InfoFile = "$publish_infoDir/publish.json";

$module_InfoFile = "$moduleDir/VpnHoodServer-linux.json";
$module_InstallerFile = "$moduleDir/VpnHoodServer-linux.sh";
$module_PackageFile = "$moduleDir/VpnHoodServer-linux.tar.gz";

$lineEnding = "`n";
$runtime = "linux-x64";
$launcher_exeFile = "$versionTag/VpnHoodServer";
. "$PSScriptRoot\_publish_impl.ps1";
