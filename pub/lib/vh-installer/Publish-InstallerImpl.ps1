param(
    [Parameter(Mandatory = $true)] [string]$projectDir,
    [Parameter(Mandatory = $true)] [string]$repoBaseUrl,
    [Parameter(Mandatory = $true)] [string]$publishDirName,
    [Parameter(Mandatory = $true)] [string]$os,
    [Parameter(Mandatory = $true)] [string]$cpu,
    [Parameter(Mandatory = $true)] [string]$launcherName,
    # Which install template to fill. The server takes "install"; a desktop client takes
    # "install-client", which writes a headless service, a PATH entry and a desktop entry instead
    # of a server's single daemon. Named rather than switched so a fork can add its own.
    [Parameter(Mandatory = $false)] [string]$installTemplate = "install",
    # Where the app's logo sits inside assets/ui.zip, for the desktop entry's icon. Only the client
    # templates read it; this repo ships no second copy of the picture to point at instead.
    [Parameter(Mandatory = $false)] [string]$logoAssetPath = ""
)
 
$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
$runtime = "$os-$cpu";

# Init script
. "$SolutionDir/pub/lib/Common.ps1";

# Get project information, as MSBuild evaluates it (Get-ProjectProperty): a client head takes both
# names from the app's identity, which its csproj does not spell out.
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;
$assemblyName = Get-ProjectProperty $projectFile "AssemblyName";
$productName = Get-ProjectProperty $projectFile "Product";
if (-not $assemblyName) { throw "The project has no AssemblyName: '$projectFile'." };

Write-Host;
Write-Host "*** Creating $assemblyName-$runtime Module ..." -BackgroundColor Blue -ForegroundColor White;

# Build the release URL only AFTER Common.ps1 is sourced: $versionTag is defined there (via
# Update-VersionFile.ps1). Computing it earlier leaves the version segment empty on the first call and
# stale on the second (both invocations share one dot-sourced scope), producing broken URLs like
# ".../releases/download//VpnHoodServer-linux-x64.sh".
$releaseUrl = "$repoBaseUrl/releases/download/$versionTag";

#update project version
UpdateProjectVersion $projectFile;

# prepare module folders
$moduleDir = "$packagesRootDir/$publishDirName/$os-$cpu";
$moduleDirLatest = "$packagesRootDirLatest/$publishDirName/$os-$cpu";
PrepareModuleFolder $moduleDir $moduleDirLatest;

# extensions
$shellExt = if ($os -ieq "linux") { "sh" } else { "ps1" };
$packageFileExt = if ($os -ieq "linux") { "tar.gz" } else { "zip" };

# Creating package
$templateDir = "$PSScriptRoot/$os";
$template_installScriptFile = "$templateDir/$installTemplate.$shellExt";
if (-not (Test-Path $template_installScriptFile)) { throw "Install template not found: $template_installScriptFile"; }
$template_launcherFile = "$templateDir/vhlauncher.$shellExt";
$template_updaterFile = "$templateDir/updater.$shellExt";

$publishDir = "$projectDir/bin/release/publish-$runtime";
$publishFileExt = if ($os -ieq "linux") { "" } else { ".ps1" };
$publish_infoDir = "$publishDir/$versionTag/publish_info";
$publish_updaterFileName = "vhupdate$publishFileExt";
$publish_launcherFileName = "$launcherName$publishFileExt";
$publish_updaterFile = "$publish_infoDir/$publish_updaterFileName";
$publish_launcherFile = "$publish_infoDir/$publish_launcherFileName";
$publish_infoFile = "$publish_infoDir/publish.json";

$module_infoFile = "$moduleDir/$assemblyName-$runtime.json";
$module_InstallerFile = "$moduleDir/$assemblyName-$runtime.$shellExt";
$module_packageFile = "$moduleDir/$assemblyName-$runtime.$packageFileExt";

$lineEnding = "`n";
$launcher_exeFile = "$versionTag/$assemblyName";

# Calcualted Path
$module_infoFileName = $(Split-Path "$module_infoFile" -leaf);
$module_packageFileName = $(Split-Path "$module_packageFile" -leaf);
$module_installerFileName = $(Split-Path "$module_InstallerFile" -leaf);
$module_installerUrl = "$releaseUrl/$module_installerFileName";

# prepare publish folder
try { Remove-Item -path "$publishDir" -Force -Recurse } catch {}
New-Item -ItemType Directory -Path $publish_infoDir -Force | Out-Null;

# publish 
Write-Output "Build $module_packageFileName...";
dotnet publish $projectDir `
    -c "Release" `
    --output "$publishDir/$versionTag" `
    --framework "net10.0" `
    --self-contained `
    --runtime "$runtime" `
    -p:SolutionDir=$solutionDir `
    -p:Version=$versionParam `
    -t:Clean;

if ($LASTEXITCODE -gt 0) { Throw "The publish exited with error code: " + $lastexitcode; }


# create installation script
Write-Output "Creating installation script...";
$installScript = Get-Content -Path "$template_installScriptFile" -Raw;
$installScript = $installScript.Replace('$(releaseUrlParam)', "$releaseUrl");
$installScript = $installScript.Replace('$(packageUrlParam)', "$releaseUrl/$module_packageFileName");
$installScript = $installScript.Replace('$(versionTagParam)', "$versionTag");
$installScript = $installScript.Replace('$(productNameParam)', "$productName");
$installScript = $installScript.Replace('$(assemblyNameParam)', "$assemblyName");
$installScript = $installScript.Replace('$(launcherNameParam)', "$launcherName");
$installScript = $installScript.Replace('$(logoAssetPathParam)', "$logoAssetPath");
$installScript = $installScript -replace "`r`n", $lineEnding;
$installScript  | Out-File -FilePath "$module_InstallerFile" -Encoding ASCII -Force -NoNewline;

# launcher script
Write-Output "Creating launcher script...";
$installScript = (Get-Content -Path "$template_launcherFile" -Raw).Replace('{exeFileParam}', $launcher_exeFile);
$installScript = $installScript -replace "`r`n", $lineEnding;
$installScript  | Out-File -FilePath "$publish_launcherFile" -Encoding ASCII -Force -NoNewline;

# updater script
Write-Output "Creating updater script...";
$installScript = Get-Content -Path "$template_updaterFile" -Raw;
$installScript = $installScript -replace "`r`n", $lineEnding;
$installScript  | Out-File -FilePath "$publish_updaterFile" -Encoding ASCII -Force -NoNewline;

# publish info
$json = @{
    Version = $versionParam; 
    ExeFile = $launcher_exeFile; 
    UpdateInfoUrl = "$repoBaseUrl/releases/latest/download/$module_infoFileName";
    InstallScriptUrl = $module_installerUrl;
    UpdateCode = "5EE5047D-6E67-43D4-A90D-665813CA1E7F"
};
    
$json | ConvertTo-Json | Out-File "$publish_infoFile" -Encoding ASCII;
$json | ConvertTo-Json | Out-File "$module_infoFile" -Encoding ASCII;

# zip
Write-Output "Compressing package...";
if ("$module_packageFile" -Like "*.zip")
{
    Compress-Archive -Path "$publishDir/*" -DestinationPath "$module_packageFile" -Force -ErrorAction Stop;
}
else
{
    # Pack the version folder explicitly. Do NOT use a "*" glob: it is expanded
    # against the current working directory (not -C) and GNU tar (Linux) then
    # fails, silently producing an empty archive. Naming $versionTag is portable
    # across GNU tar and bsdtar (Windows) and keeps the same entry layout.
    tar -czf "$module_packageFile" -C "$publishDir" "$versionTag";
    if ($LASTEXITCODE -ne 0) { Throw "tar exited with error code: $LASTEXITCODE"; }
}

if ($isLatest)
{
	Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse;
}