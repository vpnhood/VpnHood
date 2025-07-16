#!/bin/bash
echo "VpnHood! CLIENT Installation for linux";

# Default arguments
packageUrl="$packageUrlParam";
versionTag="$versionTagParam";
destinationPath="/opt/VpnHoodClient";
packageFile="";

# Read arguments
for i; 
do
arg=$i;
if [ "$arg" = "-autostart" ]; then
	autostart="y";
	lastArg=""; continue;

elif [ "$arg" = "-q" ]; then
	quiet="y";
	lastArg=""; continue;

elif [ "$lastArg" = "-packageUrl" ]; then
	packageUrl=$arg;
	lastArg=""; continue;

elif [ "$lastArg" = "-packageFile" ]; then
	packageFile=$arg;
	lastArg=""; continue;

elif [ "$lastArg" = "-versionTag" ]; then
	versionTag=$arg;
	lastArg=""; continue;


elif [ "$lastArg" != "" ]; then
	echo "Unknown argument! argument: $lastArg";
	exit 1;
fi;
lastArg=$arg;
done;

# validate $versionTag
if [ "$versionTag" == "" ]; then
	echo "Could not find versionTag!";
	exit 1;
fi
binDir="$destinationPath/$versionTag";

# User interaction
if [ "$quiet" != "y" ]; then
	if [ "$autostart" == "" ]; then
		read -p "Auto Start (y/n)?" autostart;
	fi;
fi;

# download & install Moudle
if [ "$packageFile" = "" ]; then
	echo "Downloading VpnHood! CLIENT...";
	packageFile="VpnHoodClient-linux.tar.gz";
	wget -nv -O "$packageFile" "$packageUrl";
	if [ $? != 0 ]; then
		echo "Could not download $packageUrl";
		exit 1;
	fi
fi

# extract
echo "Extracting to $destinationPath";
mkdir -p $destinationPath;
tar -xzf "$packageFile" -C "$destinationPath"
if [ $? != 0 ]; then
	echo "Could not extract $packageFile";
	exit 1;
fi

# Updating shared files...
echo "Updating shared files...";
infoDir="$binDir/publish_info";
cp "$infoDir/vhupdate" "$destinationPath/" -f;
cp "$infoDir/vhclient" "$destinationPath/" -f;
cp "$infoDir/publish.json" "$destinationPath/" -f;
chmod +x "$binDir/VpnHoodClient";
chmod +x "$destinationPath/vhclient";
chmod +x "$destinationPath/vhupdate";

# init service
if [ "$autostart" = "y" ]; then
	echo "creating autostart service... Name: VpnHoodClient";
	service="
[Unit]
Description=VpnHood! CLIENT
After=network.target

[Service]
Type=simple
ExecStart="$destinationPath/vhclient"
TimeoutStartSec=0
Restart=always
RestartSec=10
StandardOutput=null

[Install]
WantedBy=default.target
";

	echo "$service" > "/etc/systemd/system/VpnHoodClient.service";

	echo "creating VpnHood Updater service... Name: VpnHoodClientUpdater";
	service="
[Unit]
Description=VpnHood! CLIENT Updater
After=network.target

[Service]
Type=simple
ExecStart="$destinationPath/vhupdate"
TimeoutStartSec=0
Restart=always
RestartSec=720min

[Install]
WantedBy=default.target
";
	echo "$service" > "/etc/systemd/system/VpnHoodClientUpdater.service";

	# Executing services
	echo "Executing VpnHoodClient services...";
	systemctl daemon-reload;
	
	systemctl enable VpnHoodClient.service;
	systemctl restart VpnHoodClient.service;
	
	systemctl enable VpnHoodClientUpdater.service;
	systemctl restart VpnHoodClientUpdater.service;
fi
