param(
	[Parameter(Mandatory=$true)] [String]$projectDir, 
	[Parameter(Mandatory=$true)] [String]$packageFileTitle,
	[Parameter(Mandatory=$true)] [String]$aipFileR,
	[Parameter(Mandatory=$true)] [String]$distribution,
	[Parameter(Mandatory=$true)] [String]$repoBaseUrl,
	[Parameter(Mandatory=$true)] [String]$installationPageUrl)

. "$PSScriptRoot/Common.ps1"

$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;

Write-Host;
Write-Host "*** Building $packageFileTitle ..." -BackgroundColor Blue -ForegroundColor White;

# Init script
$advinstallerFile = (Get-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Caphyon\Advanced Installer" -Name "InstallRoot").InstallRoot;
$advinstallerFile = Join-Path $advinstallerFile "bin\x86\AdvancedInstaller.com";
$publishDir = "$projectDir/bin/Publish-$distribution";
$aipFile= "$solutionDir/$aipFileR";
$aipFolder = Split-Path -parent $aipFile;
$targetFramework = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.TargetFramework;

#update project version
UpdateProjectVersion $projectFile;

# prepare module folders
$moduleDir = "$packagesRootDir/$packageFileTitle/windows-$distribution";
$moduleDirLatest = "$packagesRootDirLatest/$packageFileTitle/windows-$distribution";
PrepareModuleFolder $moduleDir $moduleDirLatest;

# calculated path
$module_infoFile = "$moduleDir/$packageFileTitle-win-x64.json";
$module_packageFile = [System.IO.Path]::ChangeExtension($module_infoFile, ".msi");
$module_updaterConfigFile= [System.IO.Path]::ChangeExtension($module_infoFile, ".txt");
$module_infoFileName = $(Split-Path "$module_infoFile" -leaf);
$module_packageFileName = $(Split-Path "$module_packageFile" -leaf);

# publish 
Write-Host;
if (-not $noclean)  { dotnet clean "$projectDir" -c "Release" --output $publishDir /verbosity:$msverbosity }
dotnet publish "$projectDir" -c "Release" --output $publishDir --framework $targetFramework --self-contained --runtime "win-x64" /p:Version=$versionParam;
if ($LASTEXITCODE -gt 0) { Throw "The publish exited with error code: " + $lastexitcode; }

# Build Setup
$buildPacakgeFile = "$aipFolder/release/$packageFileTitle-win-x64.msi";
& $advinstallerFile /build "$aipFile";

#####
# copy to module
Copy-Item -path "$buildPacakgeFile" -Destination "$moduleDir/" -Force;

# publish info
$json = @{
    Version = $versionParam; 
    UpdateInfoUrl = "$repoBaseUrl/releases/latest/download/$module_infoFileName";
    PackageUrl = "$repoBaseUrl/releases/download/$versionTag/$module_packageFileName";
	InstallationPageUrl = "$installationPageUrl";
	ReleaseDate = "$releaseDate";
	DeprecatedVersion = "$deprecatedVersion";
	NotificationDelay = "3.00:00:00"
};
$json | ConvertTo-Json | Out-File "$module_infoFile" -Encoding ASCII;

# Create Updater Config File
$str=";aiu;

[Update]
Name = VpnHood $versionParam
ProductVersion = $versionParam
URL = $repoBaseUrl/releases/download/$versionTag/$module_packageFileName
Size = $((Get-Item $module_packageFile).length)
SHA256 = $((Get-FileHash $module_packageFile -Algorithm SHA256).Hash)
MD5 = $((Get-FileHash $module_packageFile -Algorithm MD5).Hash)
ServerFileName = $module_packageFileName
Flags = NoRedetect
RegistryKey = HKUD\Software\VpnHood\$packageFileTitle\Version
Version = $versionParam
UpdatedApplications = VpnHood(1.0-$versionParam)
Description = <a href=""https://github.com/vpnhood/VpnHood/blob/main/CHANGELOG.md"">Release note</a>
";
$str | Out-File -FilePath $module_updaterConfigFile;


if ($isLatest)
{
	Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse;
}
