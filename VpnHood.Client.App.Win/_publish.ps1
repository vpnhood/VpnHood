. "$PSScriptRoot/../Pub/Common.ps1"

Write-Host;
Write-Host "*** Building Client Windows ..." -BackgroundColor Blue -ForegroundColor White;
$module_title = "VpnHoodClient-win-x64";

# Init script
$advinstallerFile = (Get-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Caphyon\Advanced Installer" -Name "InstallRoot").InstallRoot;
$advinstallerFile = Join-Path $advinstallerFile "bin\x86\AdvancedInstaller.com";
$projectDir = $PSScriptRoot;
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;
$publishDir = "$projectDir/bin/release/publish";
$aipFile= "$solutionDir/VpnHood.Client.App.Win.Setup/VpnHood.Client.App.Win.Setup.aip";
$targetFramework = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.TargetFramework;

#update project version
UpdateProjectVersion $projectFile;

# prepare module folders
$moduleDir = "$packagesClientDir/windows";
$moduleDirLatest = "$packagesClientDirLatest/windows";
PrepareModuleFolder $moduleDir $moduleDirLatest;

# calculated path
$module_infoFileName = "$module_title.json";
$module_infoFile = "$moduleDir/$module_infoFileName";
$module_packageFileName = "$module_title.exe";
$module_packageFile = "$solutionDir/VpnHood.Client.App.Win.Setup/release/$module_packageFileName";
$module_updaterConfigFile= "$moduleDir/$module_title.txt";

# publish 
Write-Host;
if (-not $noclean)  { dotnet clean "$projectDir" -c "Release" --output $publishDir /verbosity:$msverbosity }
dotnet publish "$projectDir" -c "Release" --output $publishDir --framework $targetFramework --self-contained --runtime "win-x64" /p:Version=$versionParam;
if ($LASTEXITCODE -gt 0) { Throw "The publish exited with error code: " + $lastexitcode; }

# Build Setup
& $advinstallerFile /build "$aipFile";

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
$str | Out-File -FilePath $module_updaterConfigFile;


# publish info
$json = @{
    Version = $versionParam; 
    UpdateInfoUrl = "https://github.com/vpnhood/VpnHood/releases/latest/download/$module_infoFileName";
    PackageUrl = "https://github.com/vpnhood/VpnHood/releases/download/$versionTag/$module_packageFileName";
	InstallationPageUrl = "https://github.com/vpnhood/VpnHood/wiki/Install-VpnHood-Client";
	ReleaseDate = "$releaseDate";
	DeprecatedVersion = "$deprecatedVersion";
	NotificationDelay = "3.00:00:00"
};
$json | ConvertTo-Json | Out-File "$module_infoFile" -Encoding ASCII;

#####
# copy to module
Copy-Item -path "$module_packageFile" -Destination "$moduleDir/" -Force;

if ($isLatest)
{
	Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse;
}
