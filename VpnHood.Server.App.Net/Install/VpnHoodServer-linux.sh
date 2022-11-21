#!/bin/bash
echo "VpnHood Installation for linux";

# Default arguments
packageUrl="$packageUrlParam";
destinationPath="/opt/VpnHoodServer";
packageFile="";

# Read arguments
for i; 
do
if [ "$i" = "-autostart" ]; then
	autostart="y";
	lastArg=""; continue;

elif [ "$i" = "-q" ]; then
	setDotNet="y";
	quiet="y";
	lastArg=""; continue;

elif [ "$lastArg" = "-restBaseUrl" ]; then
	restBaseUrl=$i;
	lastArg=""; continue;

elif [ "$lastArg" = "-restAuthorization" ]; then
	restAuthorization=$i;
	lastArg=""; continue;

elif [ "$lastArg" = "-packageFile" ]; then
	packageFile=$i;
	lastArg=""; continue;

elif [ "$lastArg" != "" ]; then
	echo "Unknown argument! argument: $lastArg";
	exit;
fi;
lastArg=$i;
done;

# User interaction
if [ "$quiet" != "y" ]; then
	read -p "Auto Start (y/n)?" autostart;
fi;

# point to latest version if $packageUrl is not set
if [ "$packageUrl" = "" ]; then
	packageUrl="https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodServer-linux.tar.gz";
fi

# download & install VpnHoodServer
if [ "$packageFile" = "" ]; then
	echo "Downloading VpnHoodServer...";
	packageFile="VpnHoodServer-linux.tar.gz";
	wget -O $packageFile $packageUrl;
fi

echo "Extracting to $destinationPath";
mkdir -p $destinationPath;
tar -xzvf "$packageFile" -C /opt/VpnHoodServer
chmod +x "$destinationPath/vhserver"

# init service
if [ "$autostart" = "y" ]; then
	echo "creating autostart service. Name: VpnHoodService...";
	service="
[Unit]
Description=VpnHood Server
After=network.target

[Service]
Type=simple
ExecStart="$destinationPath/vhserver"
ExecStop="$destinationPath/vhserver" stop
TimeoutStartSec=0
Restart=always
RestartSec=10

[Install]
WantedBy=default.target
";

	echo "$service" > "/etc/systemd/system/VpnHoodServer.service";

	# run service
	echo "run VpnHoodServer service...";
	systemctl daemon-reload;
	systemctl enable VpnHoodServer.service;
	systemctl restart VpnHoodServer.service;
fi

# Write AppSettingss
if [ "$restBaseUrl" != "" ]; then
appSettings="{
  \"HttpAccessServer\": {
    \"BaseUrl\": \"$restBaseUrl\",
    \"Authorization\": \"$restAuthorization\"
  }
}
";
echo "$appSettings" > "$destinationPath/appsettings.json"
fi

