Write-Host;
Write-Host "*** Creating Windows Server Module..." -BackgroundColor Blue -ForegroundColor White;

# Init script
$projectDir = Split-Path $PSScriptRoot -Parent;
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;
. "$projectDir/../Pub/Common.ps1";

#update project version
UpdateProjectVersion $projectFile;

# prepare module folders
$moduleDir = "$packagesServerDir/win-x64";
$moduleDirLatest = "$packagesServerDirLatest/win-x64";
PrepareModuleFolder $moduleDir $moduleDirLatest;

# Creating linux package
$templateDir = "$PSScriptRoot/Win";
$template_installScriptFile = "$templateDir/install.ps1";
$template_launcherFile = "$templateDir/vhserver.ps1";
$template_updaterFile = "$templateDir/updater.ps1";

$publishDir = "$projectDir/bin/release/publish-win";
$publish_infoDir = "$publishDir/$versionTag/publish_info";
$publish_updaterFile = "$publish_infoDir/vhupdate.ps1";
$publish_launcherFile = "$publish_infoDir/vhserver.ps1";
$publish_infoFile = "$publish_infoDir/publish.json";

$module_infoFile = "$moduleDir/VpnHoodServer-win-x64.json";
$module_InstallerFile = "$moduleDir/VpnHoodServer-win-x64.ps1";
$module_packageFile = "$moduleDir/VpnHoodServer-win-x64.zip";

$lineEnding = "`r`n";
$runtime = "win-x64";
$launcher_exeFile = "$versionTag/VpnHoodServer.exe";

. "$PSScriptRoot\publish_impl.ps1";
