#!/bin/bash
echo "VpnHood Installation for linux";

# Default arguments
installUrl="$installUrlParam";
destinationPath="/opt/VpnHoodServer";

# Read arguments
for i; 
do
if [ "$i" = "-autostart" ]; then
	autostart="y";
	lastArg=""; continue;

elif [ "$i" = "-q" ]; then
	quiet="y";
	lastArg=""; continue;

elif [ "$lastArg" = "-secret" ]; then
	secret=$i;
	lastArg=""; continue;

elif [ "$lastArg" = "-restBaseUrl" ]; then
	restBaseUrl=$i;
	lastArg=""; continue;

elif [ "$lastArg" = "-restAuthorization" ]; then
	restAuthorization=$i;
	lastArg=""; continue;

elif [ "$lastArg" = "-restAuthorization" ]; then
	restAuthorization=$i;
	lastArg=""; continue;


elif [ "$lastArg" != "" ]; then
	echo "Unknown argument! argument: $lastArg";
	exit;
fi;
lastArg=$i;
done;

# User interaction
if [ "$quiet" != "y" ]; then
	read -p "Set dotnet alias to .NET 5.0 (y/n)?" setDotNet;
	read -p "Auto Start (y/n)?" autostart;
fi;

# point to latest version if $installUrl is not set
if [ "$installUrl" = "" ]; then
	installUrl="https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodServer.zip";
fi

# install dotnet
#snap install dotnet-runtime-50 --classic <note: service runner should be changed to>
#snap alias dotnet-runtime-50.dotnet dotnet50
if [ "$setDotNet" = "y" ]; then
	snap install dotnet-sdk --classic --channel=latest/edge
	#snap unalias dotnet
	#snap alias dotnet-runtime-50.dotnet dotnet
fi

# install unzip
echo "Installing unzip...";
apt install unzip

# download & install VpnHoodServer
if [ "$packageFile" = "" ]; then
	echo "Downloading VpnHoodServer...";
	packageFile="VpnHoodServer.zip";
	wget -O $packageFile $installUrl;
fi

echo "Stop VpnHoodServer if exists...";
systemctl stop VpnHoodServer.service;

echo "Extracting to $destinationPath";
mkdir -p $destinationPath;
unzip -o VpnHoodServer.zip -d $destinationPath;
rm VpnHoodServer.zip

# init service
if [ "$autostart" = "y" ]; then
	echo "creating autostart service. Name: VpnHoodService...";
	service="
[Unit]
Description=VpnHood Server
After=network.target

[Service]
Type=simple
ExecStart=sh -c \"dotnet '$destinationPath/launcher/run.dll' -launcher:noLaunchAfterUpdate && sleep 10s\"
ExecStop=sh -c \"dotnet '$destinationPath/launcher/run.dll' stop\"
TimeoutStartSec=0
Restart=always
RestartSec=2

[Install]
WantedBy=default.target
";
	echo "$service" > "/etc/systemd/system/VpnHoodServer.service";

	# run service
	echo "run VpnHoodServer service...";
	systemctl daemon-reload;
	systemctl enable VpnHoodServer.service;
	systemctl start VpnHoodServer.service;
fi

# Write AppSettingss
if [ "$restBaseUrl" != "" ]; then
appSettings="{
  \"RestAccessServer\": {
    \"BaseUrl\": \"$restBaseUrl\",
    \"Authorization\": \"$restAuthorization\"
  },
  \"Secret\": \"$secret\"
}
";
echo "$appSettings" > "$destinationPath/appsettings.json"
fi

