$projectDir = Split-Path -parent $PSScriptRoot;
$versionParam = "1.1.0";
$versionTag = "$versionParam-debug";
$userPrivateKeyFile="C:\Users\Developer\Desktop\work\SshKeys\ubuntu-vps-ssh.openssh";
$configFolder = (Split-Path -parent (Split-Path -parent $projectDir)) + "\.user\access.vpnhood.com";
$remoteHost = "15.204.131.99";
$remoteUser = "ubuntu";
$remoteDir = "/opt/VpnHoodAgent";
$exeFileName="VpnHood.AccessServer.Agent";
$executerFileName="vhagent";

# calcualte
$publishDir = Join-Path -Path $projectDir -ChildPath "bin/publish";
$outputDir = Join-Path -Path $publishDir -ChildPath $versionTag;
$runtime = "linux-x64";
$remote = "$remoteUser@" + "$remoteHost";

# publish 
Write-Host "Building app..." -ForegroundColor Blue;
if (Test-Path -Path $publishDir -PathType Container) {Remove-Item -path "$publishDir" -Force -Recurse};
if (-not $noclean)  { dotnet clean "$projectDir" -c "Release" --output $publishDir | Out-Null }
# dotnet publish "$projectDir" -c "Release" --output $outputDir --framework "net8.0" --self-contained --runtime "$runtime" /p:Version=$versionParam;
if ($LASTEXITCODE -gt 0) { Throw "The publish exited with error code: " + $lastexitcode; }

# --------
Write-Host "Copying app files..." -ForegroundColor Blue;
$remoteDest = "$remote" +":$remoteDir";
scp -r -i $userPrivateKeyFile $outputDir $remoteDest

# set execute permission
 ssh -i $userPrivateKeyFile $remote "chmod +x $remoteDir/$versionTag/$exeFileName";

# --------
Write-Host "Copying configuration files..." -ForegroundColor Blue;
$remoteDest = "$remote" + ":$remoteDir";
scp -r -i $userPrivateKeyFile "$configFolder/*" $remoteDest

# --------
Write-Host "Creating executer bash file..." -ForegroundColor Blue;
$executerFile = "$outputDir/$executerFileName";
$script = '#!/bin/bash
cd "$(dirname "$0")";
exeFile="./{exeFileR}";
chmod +x "$exeFile";
"$exeFile" "$@";' -replace "{exeFileR}", "$versionTag/$exeFileName";

New-Item -ItemType Directory -Path $outputDir -Force > $null
$script | Out-File "$executerFile" -Encoding ASCII;

# upload executer
$remoteDest = "$remote" +":$remoteDir/$executerFileName";
scp -r -i $userPrivateKeyFile $executerFile $remoteDest
ssh -i $userPrivateKeyFile $remote "sudo chmod +x $remoteDir/$executerFileName";

# restart service
