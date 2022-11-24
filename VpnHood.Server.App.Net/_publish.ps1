. "$PSScriptRoot\..\Pub\Common.ps1";

Write-Host;
Write-Host "*** Creating Linux Server Module..." -BackgroundColor Blue -ForegroundColor White;

# prepare module folders
$moduleDir = "$packagesServerDir/linux-x64";
$moduleDirLatest = "$packagesServerDirLatest/linux-x64";
PrepareModuleFolder $moduleDir $moduleDirLatest;

$projectDir = $PSScriptRoot;
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;

# Creating linux package
$templateDir = "$PSScriptRoot/Install/Linux";
$template_installScriptFile = "$templateDir/install.sh";
$template_launcherFile = "$templateDir/vhserver.sh";
$template_updaterFile = "$templateDir/updater.sh";

$publishDir = "$projectDir/bin/release/publish-linux";
$publish_infoDir = "$publishDir/$versionTag/publish_info";
$publish_updaterFile = "$publish_infoDir/update";
$publish_launcherFile = "$publish_infoDir/vhserver";
$publish_InfoFile = "$publish_infoDir/publish.json";

$module_InfoFile = "$moduleDir/VpnHoodServer-linux.json";
$module_InstallerFile = "$moduleDir/VpnHoodServer-linux.sh";
$module_PackageFile = "$moduleDir/VpnHoodServer-linux.tar.gz";

# Calcualted Path
$module_InfoFileName = $(Split-Path "$module_InfoFile" -leaf);
$module_PackageFileName = $(Split-Path "$module_PackageFile" -leaf);
$module_InstallerFileName = $(Split-Path "$module_InstallerFile" -leaf);

# prepare publish folder
try { Remove-Item -path "$publishDir" -Force -Recurse } catch {}
New-Item -ItemType Directory -Path $publish_infoDir -Force | Out-Null;

# publish 
echo "Build Server...";
if (-not $noclean)  { dotnet clean "$projectDir" -c "Release" --output $publishDir | Out-Null }
dotnet publish "$projectDir" -c "Release" --output "$publishDir/$versionTag" --framework "net7.0" --self-contained --runtime "linux-x64" /p:Version=$versionParam
if ($LASTEXITCODE -gt 0) { Throw "The publish exited with error code: " + $lastexitcode; }

# create installation script
echo "Creating Server installation script...";
$linuxScript = Get-Content -Path "$template_installScriptFile" -Raw;
$linuxScript = $linuxScript.Replace('$packageUrlParam', "https://github.com/vpnhood/VpnHood/releases/download/$versionTag/$module_packageFileName");
$linuxScript = $linuxScript.Replace('$versionTagParam', "$versionTag");
$linuxScript = $linuxScript -replace "`r`n", "`n";
$linuxScript  | Out-File -FilePath "$module_InstallerFile" -Encoding ASCII -Force -NoNewline;

# launcher script
echo "Creating Server launcher script...";
$exeFile = "$versionTag/VpnHoodServer";
$linuxScript = (Get-Content -Path "$template_launcherFile" -Raw).Replace('{exeFileParam}', $exeFile);
$linuxScript = $linuxScript -replace "`r`n", "`n";
$linuxScript  | Out-File -FilePath "$publish_launcherFile" -Encoding ASCII -Force -NoNewline;

# updater script
echo "Creating Server updater script...";
Copy-Item -path "$template_updaterFile" -Destination "$publish_updaterFile" -Force -Recurse

# publish info
$json = @{
    Version=$versionParam; 
    ExeFile=$exeFile; 
    UpdateInfoUrl="https://github.com/vpnhood/VpnHood/releases/latest/download/$module_InfoFileName";
    InstallScriptUrl="https://github.com/vpnhood/VpnHood/releases/download/$versionTag/$module_InstallerFileName";
    UpdateCode="5EE5047D-6E67-43D4-A90D-665813CA1E7F"
    };
    
$json | ConvertTo-Json | Out-File "$publish_InfoFile" -Encoding utf8;
$json | ConvertTo-Json | Out-File "$module_InfoFile" -Encoding utf8;

# zip
echo "Compressing Server package...";
tar -czf "$module_PackageFile" -C "$publishDir\" *;

if ($isLatest)
{
	Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse
}