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
$publishDir = "$projectDir/bin/release/publish-linux";
$packageFileName = "VpnHoodServer-linux.tar.gz";
$installScriptFileName = "VpnHoodServer-linux.sh";
$publishInfoPackageFileName = "VpnHoodServer-linux.json";
$releaseInfoDir = "$publishDir/$versionTag/publish_info";
$publishInfoFileName = "publish.json";
$launcherFileName = "vhserver";

# publish 
echo "Build Linux Server...";
if (-not $noclean)  { dotnet clean "$projectDir" -c "Release" --output $publishDir | Out-Null }
try { Remove-Item -path "$publishDir" -Force -Recurse } catch {}
dotnet publish "$projectDir" -c "Release" --output "$publishDir/$versionTag" --framework "net7.0" --self-contained --runtime "linux-x64" /p:Version=$versionParam
if ($LASTEXITCODE -gt 0) { Throw "The publish exited with error code: " + $lastexitcode; }
New-Item -ItemType Directory -Path $releaseInfoDir -Force | Out-Null;

# server install-linux.sh
echo "Creating Linux Server installation script...";
$linuxScript = Get-Content -Path "$PSScriptRoot/Install/$installScriptFileName" -Raw;
$linuxScript = $linuxScript.Replace('$packageUrlParam', "https://github.com/vpnhood/VpnHood/releases/download/$versionTag/$packageFileName");
$linuxScript = $linuxScript.Replace('$versionTagParam', "$versionTag");
$linuxScript = $linuxScript -replace "`r`n", "`n";
$linuxScript  | Out-File -FilePath "$moduleDir/$installScriptFileName" -Encoding ASCII -Force -NoNewline;

# launcher script
echo "Creating Linux Server launcher script...";
$exeFile = "$versionTag/VpnHoodServer";
$linuxScript = (Get-Content -Path "$PSScriptRoot/Install/VpnHoodServer-linux.run.sh" -Raw).Replace('{exeFileParam}', $exeFile);
$linuxScript = $linuxScript -replace "`r`n", "`n";
$linuxScript  | Out-File -FilePath "$releaseInfoDir/$launcherFileName" -Encoding ASCII -Force -NoNewline;

# publish info
$json = @{
    Version=$versionParam; 
    ExeFile=$exeFile; 
    UpdateInfoUrl="https://github.com/vpnhood/VpnHood/releases/latest/download/$publishInfoPackageFileName";
    UpdateScriptUrl="https://github.com/vpnhood/VpnHood/releases/latest/download/$installScriptFileName";
    };
    
$json | ConvertTo-Json | Out-File "$releaseInfoDir/$publishInfoFileName" -Encoding utf8;
$json | ConvertTo-Json | Out-File "$moduleDir/$publishInfoPackageFileName" -Encoding utf8;

# zip
echo "Compressing Linux Server package...";
tar -czf $moduleDir/$packageFileName -C "$publishDir\" *;

if ($isLatest)
{
	Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse
}