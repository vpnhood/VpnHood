param( 
	[Parameter(Mandatory=$true)] $AppName,
	[Parameter(Mandatory=$true)] $projectDir,
	[Parameter(Mandatory=$true)] $configDir,
	[Parameter(Mandatory=$true)] $userPrivateKeyFile,
	[Parameter(Mandatory=$true)] $remoteHost,
	[Parameter(Mandatory=$true)] $remoteUser,
	[Parameter(Mandatory=$true)] $executerFileName,
	[Parameter(Mandatory=$true)] [int]$bump,
	[Parameter(Mandatory=$true)] [bool][switch]$install_service
); 

. "$PSScriptRoot/VersionBump.ps1" -versionFile "$PSScriptRoot/PubVersion.json" -bump $bump;
	
# calcualte
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;
$assemblyName = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.AssemblyName;
if ($assemblyName -eq $null) {$assemblyName = (Get-Item $projectFile).BaseName};
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
ssh -i $userPrivateKeyFile $remote "chmod +x $remoteDir/$versionTag/$assemblyName";

# --------
Write-Host "Copying configuration files..." -ForegroundColor Blue;
$remoteDest = "$remote" + ":$remoteDir";
scp -r -i $userPrivateKeyFile "$configDir/*" $remoteDest

# --------
Write-Host "Creating executer bash file..." -ForegroundColor Blue;
$executerFile = "$outputDir/$executerFileName";
$script = '#!/bin/bash
cd "$(dirname "$0")";
exeFile="./{exeFileR}";
chmod +x "$exeFile";
"$exeFile" "$@";' -replace "{exeFileR}", "$versionTag/$assemblyName" -replace "`r`n", "`n";

New-Item -ItemType Directory -Path $outputDir -Force > $null
$script | Out-File "$executerFile" -Encoding ASCII -NoNewline;

# upload executer
$remoteDest = "$remote" +":$remoteDir/$executerFileName";
scp -r -i $userPrivateKeyFile $executerFile $remoteDest
ssh -i $userPrivateKeyFile $remote "sudo chmod +x $remoteDir/$executerFileName";

# installing service
if ($install_service) {
	Write-Host "Install $AppName service..." -ForegroundColor Blue;
	ssh -i $userPrivateKeyFile $remote "sudo cp '$remoteDir/$versionTag/$serviceFileName' '/etc/systemd/system/'";
	ssh -i $userPrivateKeyFile $remote "sudo systemctl daemon-reload; sudo systemctl enable $serviceFileName; sudo systemctl restart $serviceFileName;"
}

# restarting the service
Write-Host "Restarting $AppName service..." -ForegroundColor Blue;
ssh -i $userPrivateKeyFile $remote "sudo systemctl restart $serviceFileName;"
