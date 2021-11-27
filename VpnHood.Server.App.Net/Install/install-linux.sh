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

elif [ "$i" = "-install-dotnet" ]; then
	setDotNet="y";
	lastArg=""; continue;

elif [ "$i" = "-q" ]; then
	setDotNet="y";
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

elif [ "$lastArg" != "" ]; then
	echo "Unknown argument! argument: $lastArg";
	exit;
fi;
lastArg=$i;
done;

# User interaction
if [ "$quiet" != "y" ]; then
	read -p "Install .NET 6.0 (y/n)?" setDotNet;
	read -p "Auto Start (y/n)?" autostart;
fi;

# point to latest version if $installUrl is not set
if [ "$installUrl" = "" ]; then
	installUrl="https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodServer.tar.gz";
fi

# install dotnet
if [ "$setDotNet" = "y" ]; then
	snap remove dotnet-sdk;
	snap install dotnet-sdk --classic --channel=6.0;
	snap alias dotnet-sdk.dotnet dotnet;
fi

# download & install VpnHoodServer
if [ "$packageFile" = "" ]; then
	echo "Downloading VpnHoodServer...";
	packageFile="VpnHoodServer.tar.gz";
	wget -O $packageFile $installUrl;
fi

echo "Stop VpnHoodServer if exists...";
systemctl stop VpnHoodServer.service;

echo "Extracting to $destinationPath";
mkdir -p $destinationPath;
tar -xzvf VpnHoodServer.tar.gz -C /opt/VpnHoodServer
rm VpnHoodServer.tar.gz

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

