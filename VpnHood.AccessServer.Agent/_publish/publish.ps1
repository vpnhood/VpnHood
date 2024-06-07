$versionParam = "1.1.0";
$versionTag = "$versionParam-debug";
$userPrivateKeyFile="C:\Users\Developer\Desktop\work\SshKeys\ubuntu-vps-ssh.openssh";
$remoteHost = "15.204.131.99";
$remoteUser = "ubuntu";
$remoteDir = "/opt/VpnHoodAgent";
$exeFile="VpnHood.AccessServer.Agent";
$executerFile="vhagent";

$projectDir = Split-Path -parent $PSScriptRoot;
$publishDir = Join-Path -Path $projectDir -ChildPath "bin/publish";
$outputDir = Join-Path -Path $publishDir -ChildPath $versionTag;
$runtime = "linux-x64";

# publish 
Write-Output "Build Server...";
if (Test-Path -Path $publishDir -PathType Container) {Remove-Item -path "$publishDir" -Force -Recurse};
if (-not $noclean)  { dotnet clean "$projectDir" -c "Release" --output $publishDir | Out-Null }
#dotnet publish "$projectDir" -c "Release" --output $outputDir --framework "net8.0" --self-contained --runtime "$runtime" /p:Version=$versionParam;
if ($LASTEXITCODE -gt 0) { Throw "The publish exited with error code: " + $lastexitcode; }

# upload
$remote = "$remoteUser@" + "$remoteHost" +":$remoteDir";
#scp -r -i $userPrivateKeyFile $outputDir $remote

# set execute permission
$remote = "$remoteUser@" + "$remoteHost";
# ssh -i $userPrivateKeyFile $remote "chmod +x $remoteDir/$versionTag/$exeFile";


# update executer file
$script = '
#!/bin/bash
curDir="$(dirname "$0")";
exeFile="$curDir/$exeFileR";chmod +x "$exeFile";
"$exeFile" "$@";'
New-Item -ItemType Directory -Path $outputDir -Force
$script | Out-File "$outputDir/$executerFile" -Encoding ASCII;
echo $outputDir/$executerFile
