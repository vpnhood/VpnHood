param(
    [Parameter(Mandatory = $true)] [string]$projectDir,
    [Parameter(Mandatory = $true)] [string]$repoBaseUrl,
    [Parameter(Mandatory = $true)] [string]$publishDirName,
    [Parameter(Mandatory = $true)] [string]$os,
    [Parameter(Mandatory = $true)] [string]$cpu,
    [Parameter(Mandatory = $true)] [string]$launcherName
)
 
$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
$runtime = "$os-$cpu";

# Get project infomration
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;
$assemblyName = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.AssemblyName[0];
$productName = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.Product[0];
if (-not $assemblyName) { throw "AssemblyName not found in project file '$projectFile'. Please define an <AssemblyName> property in the .csproj." };

Write-Host;
Write-Host "*** Creating $assemblyName-$runtime Module ..." -BackgroundColor Blue -ForegroundColor White;

# Init script
. "$SolutionDir/Pub/Core/Common.ps1";

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
$template_installScriptFile = "$templateDir/install.$shellExt";
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
$module_installerUrl = "$repoBaseUrl/releases/download/$versionTag/$module_installerFileName";

# prepare publish folder
try { Remove-Item -path "$publishDir" -Force -Recurse } catch {}
New-Item -ItemType Directory -Path $publish_infoDir -Force | Out-Null;

# publish 
Write-Output "Build $module_packageFileName...";
dotnet publish $projectDir `
    -c "Release" `
    --output "$publishDir/$versionTag" `
    --framework "net9.0" `
    --self-contained `
    --runtime "$runtime" `
    -p:SolutionDir=$solutionDir `
    -p:Version=$versionParam `
    -t:Clean;

if ($LASTEXITCODE -gt 0) { Throw "The publish exited with error code: " + $lastexitcode; }


# create installation script
Write-Output "Creating installation script...";
$installScript = Get-Content -Path "$template_installScriptFile" -Raw;
$installScript = $installScript.Replace('$(packageUrlParam)', "$repoBaseUrl/releases/download/$versionTag/$module_packageFileName");
$installScript = $installScript.Replace('$(versionTagParam)', "$versionTag");
$installScript = $installScript.Replace('$(productNameParam)', "$productName");
$installScript = $installScript.Replace('$(assemblyNameParam)', "$assemblyName");
$installScript = $installScript.Replace('$(launcherNameParam)', "$launcherName");
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
    tar -czf "$module_packageFile" -C "$publishDir/" *;
}

if ($isLatest)
{
	Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse;
}