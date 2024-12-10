Write-Host;
Write-Host "*** Creating Linux Server Module..." -BackgroundColor Blue -ForegroundColor White;

# Init script
$projectDir = Split-Path $PSScriptRoot -Parent;
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;
. "$projectDir/../Pub/Core/Common.ps1";

#update project version
UpdateProjectVersion $projectFile;

# prepare module folders
$moduleDir = "$packagesRootDir/$packageServerDirName/linux-arm64";
$moduleDirLatest = "$packagesRootDirLatest/$packageServerDirName/linux-arm64";
PrepareModuleFolder $moduleDir $moduleDirLatest;

# Creating linux package
$templateDir = "$PSScriptRoot/Linux";
$template_installScriptFile = "$templateDir/install.sh";
$template_launcherFile = "$templateDir/vhserver.sh";
$template_updaterFile = "$templateDir/updater.sh";

$publishDir = "$projectDir/bin/release/publish-linux";
$publish_infoDir = "$publishDir/$versionTag/publish_info";
$publish_updaterFile = "$publish_infoDir/vhupdate";
$publish_launcherFile = "$publish_infoDir/vhserver";
$publish_infoFile = "$publish_infoDir/publish.json";

$module_infoFile = "$moduleDir/VpnHoodServer-linux-arm64.json";
$module_InstallerFile = "$moduleDir/VpnHoodServer-linux-arm64.sh";
$module_packageFile = "$moduleDir/VpnHoodServer-linux-arm64.tar.gz";

$lineEnding = "`n";
$runtime = "linux-arm64";
$launcher_exeFile = "$versionTag/VpnHoodServer";

. "$PSScriptRoot\publish_impl.ps1";
