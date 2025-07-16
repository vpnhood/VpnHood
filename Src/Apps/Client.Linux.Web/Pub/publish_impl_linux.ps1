$SolutionDir = Split-Path -Parent (Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot)));

Write-Host;
Write-Host "*** Creating Linux-$cpu Client Module ..." -BackgroundColor Blue -ForegroundColor White;

# Init script
$projectDir = Split-Path $PSScriptRoot -Parent;
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;
. "$SolutionDir/Pub/Core/Common.ps1";

#update project version
UpdateProjectVersion $projectFile;

# prepare module folders
$moduleDir = "$packagesRootDir/$packageClientDirName/linux-$cpu";
$moduleDirLatest = "$packagesRootDirLatest/$packageClientDirName/linux-$cpu";
PrepareModuleFolder $moduleDir $moduleDirLatest;

# Creating linux package
$templateDir = "$PSScriptRoot/Linux";
$template_installScriptFile = "$templateDir/install.sh";
$template_launcherFile = "$templateDir/vhclient.sh";
$template_updaterFile = "$templateDir/updater.sh";

$publishDir = "$projectDir/bin/release/publish-linux";
$publish_infoDir = "$publishDir/$versionTag/publish_info";
$publish_updaterFile = "$publish_infoDir/vhupdate";
$publish_launcherFile = "$publish_infoDir/vhclient";
$publish_infoFile = "$publish_infoDir/publish.json";

$module_infoFile = "$moduleDir/VpnHoodClient-linux-$cpu.json";
$module_InstallerFile = "$moduleDir/VpnHoodClient-linux-$cpu.sh";
$module_packageFile = "$moduleDir/VpnHoodClient-linux-$cpu.tar.gz";

$lineEnding = "`n";
$runtime = "linux-$cpu";
$launcher_exeFile = "$versionTag/VpnHoodClient";

. "$PSScriptRoot\publish_impl.ps1";
