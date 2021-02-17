param( 
	[Parameter(Mandatory=$true)][string]$server,
	[Parameter(Mandatory=$true)][string]$user, 
	[Parameter(Mandatory=$true)][string]$password,
	[Parameter(Mandatory=$true)][object]$install_dotnet, 
	[Parameter(Mandatory=$true)][object]$autostart
	);

Write-Host "Publish VpnHood Server from Windows to Linux" -ForegroundColor Green
Write-Host "Make sure PuTTY is installed on your Windows and SSH is configured on Linux" -ForegroundColor Green

$install_dotnet = $install_dotnet -eq "1";
$autostart = $autostart -eq "1";


$server="172.23.31.189"
$user="user"
$password="u"

$sudo="echo -e ""u\\n"" | sudo -S"
$remoteDir="/home/$user/VpnHood.Server"

$serverPath=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$login="$user@$server"

# install dotnet
if ($install_dotnet)
{
	 Write-Host "`nInstalling .NET RunTime" -ForegroundColor White -BackgroundColor Blue;
	$cmd = 
	"wget https://packages.microsoft.com/config/ubuntu/20.10/packages-microsoft-prod.deb -O packages-microsoft-prod.deb" + " && " +
	"$sudo dpkg -i packages-microsoft-prod.deb" + " && " +
	"$sudo apt-get update"  + " && " +
	"$sudo apt-get install -y apt-transport-https"  + " && " +
	"$sudo apt-get update"  + " && " +
	"$sudo apt-get install -y dotnet-runtime-5.0" + " && " +
	"rm packages-microsoft-prod.deb"
	plink $login -pw $password -ssh $cmd
}

# zip and upload
Write-Host "`nZip and Upload VpnHoodServer" -ForegroundColor White -BackgroundColor Blue;
$tempZipFile = Join-Path $Env:Temp "VpnHoodServer.zip";
tar.exe -C "$serverPath" -a -cf $tempZipFile "*"

# Extract
$remoteZipFile = "$remoteDir/VpnHoodServer.zip";
plink $login -pw $password -ssh -batch  "mkdir -p $remoteDir";
pscp -pw $password -l $user $tempZipFile "${login}:$remoteZipFile";
plink $login -pw $password -ssh -batch "unzip -o $remoteZipFile -d $remoteDir && rm '$remoteZipFile' ";

# Add auto run service
if ($autostart)
{
	Write-Host "`nCreating service" -ForegroundColor White -BackgroundColor Blue;
	$service="
	[Unit]
	Description=A description for your custom service goes here
	After=network.target

	[Service]
	Type=simple
	ExecStart=dotnet '$remoteDir/launcher/run.dll' -nowait
	TimeoutStartSec=0

	[Install]
	WantedBy=default.target
	"

	$serviceFileName="VpnHoodServer.service";
	$tempServiceFile = Join-Path $Env:Temp $serviceFileName;
	$tempServiceRemote = "$remoteDir/$serviceFileName";
	New-Item -Path $tempServiceFile -ItemType "file" -Value $service -Force;
	pscp -pw $password -l $user $tempServiceFile "${login}:$tempServiceRemote";
	plink $login -pw $password -ssh -batch "$sudo mv --force '$tempServiceRemote' '/etc/systemd/system/'";

	# run service
	Write-Host "`nRun service" -ForegroundColor White -BackgroundColor Blue;
	$cmd =  
	"$sudo systemctl daemon-reload" + " && " +
	"$sudo systemctl enable VpnHoodServer.service" + " && " +
	"$sudo systemctl start VpnHoodServer.service"
	plink $login -pw $password -ssh -batch $cmd
}
