if ("$moduleDir" -eq "") {throw "moduleDir has not been defined";}

# Calcualted Path
$module_infoFileName = $(Split-Path "$module_infoFile" -leaf);
$module_packageFileName = $(Split-Path "$module_packageFile" -leaf);
$module_InstallerFileName = $(Split-Path "$module_InstallerFile" -leaf);

# prepare publish folder
try { Remove-Item -path "$publishDir" -Force -Recurse } catch {}
New-Item -ItemType Directory -Path $publish_infoDir -Force | Out-Null;

# publish 
Write-Output "Build Server...";
if (-not $noclean)  { dotnet clean "$projectDir" -c "Release" --output $publishDir | Out-Null }
dotnet publish "$projectDir" -c "Release" --output "$publishDir/$versionTag" --framework "net7.0" --self-contained --runtime "$runtime" /p:Version=$versionParam;
if ($LASTEXITCODE -gt 0) { Throw "The publish exited with error code: " + $lastexitcode; }

# create installation script
Write-Output "Creating Server installation script...";
$linuxScript = Get-Content -Path "$template_installScriptFile" -Raw;
$linuxScript = $linuxScript.Replace('$packageUrlParam', "https://github.com/vpnhood/VpnHood/releases/download/$versionTag/$module_packageFileName");
$linuxScript = $linuxScript.Replace('$versionTagParam', "$versionTag");
$linuxScript = $linuxScript -replace "`r`n", $lineEnding;
$linuxScript  | Out-File -FilePath "$module_InstallerFile" -Encoding ASCII -Force -NoNewline;

# launcher script
Write-Output "Creating Server launcher script...";
$linuxScript = (Get-Content -Path "$template_launcherFile" -Raw).Replace('{exeFileParam}', $launcher_exeFile);
$linuxScript = $linuxScript -replace "`r`n", $lineEnding;
$linuxScript  | Out-File -FilePath "$publish_launcherFile" -Encoding ASCII -Force -NoNewline;

# updater script
Write-Output "Creating Server updater script...";
$linuxScript = Get-Content -Path "$template_updaterFile" -Raw;
$linuxScript = $linuxScript -replace "`r`n", $lineEnding;
$linuxScript  | Out-File -FilePath "$publish_updaterFile" -Encoding ASCII -Force -NoNewline;

# publish info
$json = @{
    Version = $versionParam; 
    ExeFile = $launcher_exeFile; 
    UpdateInfoUrl = "https://github.com/vpnhood/VpnHood/releases/latest/download/$module_infoFileName";
    InstallScriptUrl = "https://github.com/vpnhood/VpnHood/releases/download/$versionTag/$module_InstallerFileName";
    UpdateCode = "5EE5047D-6E67-43D4-A90D-665813CA1E7F"
};
    
$json | ConvertTo-Json | Out-File "$publish_infoFile" -Encoding ASCII;
$json | ConvertTo-Json | Out-File "$module_infoFile" -Encoding ASCII;

# zip
Write-Output "Compressing Server package...";
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
	Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse
}