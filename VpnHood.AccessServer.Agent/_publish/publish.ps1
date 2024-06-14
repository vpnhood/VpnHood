param( [Parameter(Mandatory=$true)] [switch]$install_service ) ;
$projectDir = Split-Path -parent $PSScriptRoot;
$AppName="VpnHoodAgent-stage";
$versionParam = "1.1.1";
$versionTag = "$versionParam-debug";
$configFolder = (Split-Path -parent (Split-Path -parent $projectDir)) + "\.user\access-stage.vpnhood.com";
$userPrivateKeyFile="$configFolder\ssh.openssh";
$remoteHost = "15.204.131.99";
$remoteUser = "ubuntu";
$exeFileName="VpnHood.AccessServer.Agent";
$executerFileName="vhagent";

# calcualte
$publishDir = Join-Path -Path $projectDir -ChildPath "bin/publish";
$outputDir = Join-Path -Path $publishDir -ChildPath $versionTag;
$runtime = "linux-x64";
$remote = "$remoteUser@" + "$remoteHost";
$remoteDir = "/opt/$AppName";
$serviceFileName = "$AppName.service";

# publish 
Write-Host "Building app..." -ForegroundColor Blue;
if (Test-Path -Path $publishDir -PathType Container) {Remove-Item -path "$publishDir" -Force -Recurse};
if (-not $noclean)  { dotnet clean "$projectDir" -c "Release" --output $publishDir | Out-Null }
dotnet publish "$projectDir" -c "Release" --output $outputDir --framework "net8.0" --self-contained --runtime "$runtime" /p:Version=$versionParam;
if ($LASTEXITCODE -gt 0) { Throw "The publish exited with error code: " + $lastexitcode; }

# Create Service File
$service=@"
[Unit] 
Description=$AppName
After=network.target

[Service]
Type=simple
ExecStart=$remoteDir/$executerFileName
TimeoutStartSec=0
Restart=always
RestartSec=10
StandardOutput=null

[Install]
WantedBy=default.target
"@ -replace "`r`n", "`n";

$serviceFile = "$outputDir/$serviceFileName";
$service | Out-File $serviceFile -Encoding ASCII;

# --------
Write-Host "Copying app files..." -ForegroundColor Blue;
$remoteDest = "$remote" +":$remoteDir";
ssh -i $userPrivateKeyFile $remote "sudo mkdir -p $remoteDir; sudo chown $remoteUser $remoteDir";
scp -r -i $userPrivateKeyFile $outputDir $remoteDest;
scp -r -i $userPrivateKeyFile "$outputDir/appsettings.json" $remoteDest;

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
"$exeFile" "$@";' -replace "{exeFileR}", "$versionTag/$exeFileName" -replace "`r`n", "`n";

New-Item -ItemType Directory -Path $outputDir -Force > $null
$script | Out-File "$executerFile" -Encoding ASCII;

# upload executer
$remoteDest = "$remote" +":$remoteDir/$executerFileName";
scp -r -i $userPrivateKeyFile $executerFile $remoteDest
ssh -i $userPrivateKeyFile $remote "sudo chmod +x $remoteDir/$executerFileName";

if ($install_service) {
	Write-Host "Install service..." -ForegroundColor Blue;
	ssh -i $userPrivateKeyFile $remote "sudo cp '$remoteDir/$versionTag/$serviceFileName' '/etc/systemd/system/'";
	ssh -i $userPrivateKeyFile $remote "sudo systemctl daemon-reload; sudo systemctl enable $serviceFileName; sudo systemctl restart $serviceFileName;"
}
