#!/bin/bash
echo "VpnHood Server Installation for linux";

# Default arguments
packageUrl="$packageUrlParam";
versionTag="$versionTagParam";
destinationPath="/opt/VpnHoodServer";
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

elif [ "$lastArg" = "-restBaseUrl" ]; then
	restBaseUrl=$arg;
	lastArg=""; continue;

elif [ "$lastArg" = "-restAuthorization" ]; then
	restAuthorization=$arg;
	lastArg=""; continue;

elif [ "$lastArg" = "-secret" ]; then
	secret=$arg;
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

# point to latest version if $packageUrl is not set
if [ "$packageUrl" = "" ]; then
	packageUrl="https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodServer-linux.tar.gz";
fi

# download & install VpnHoodServer
if [ "$packageFile" = "" ]; then
	echo "Downloading VpnHoodServer...";
	packageFile="VpnHoodServer-linux.tar.gz";
	wget -nv -O "$packageFile" "$packageUrl";
fi

# extract
echo "Extracting to $destinationPath";
mkdir -p $destinationPath;
tar -xzf "$packageFile" -C "$destinationPath"

# Updating shared files...
echo "Updating shared files...";
infoDir="$binDir/publish_info";
cp "$infoDir/vhupdate" "$destinationPath/" -f;
cp "$infoDir/vhserver" "$destinationPath/" -f;
cp "$infoDir/publish.json" "$destinationPath/" -f;
chmod +x "$binDir/VpnHoodServer";
chmod +x "$destinationPath/vhserver";
chmod +x "$destinationPath/vhupdate";

# Write AppSettingss
if [ "$restBaseUrl" != "" ]; then
	appSettings="{
  \"HttpAccessServer\": {
    \"BaseUrl\": \"$restBaseUrl\",
    \"Authorization\": \"$restAuthorization\"
  },
  \"Secret\": \"$secret\"
}
";
	mkdir -p "$destinationPath/storage";
	echo "$appSettings" > "$destinationPath/storage/appsettings.json"
fi

# init service
if [ "$autostart" = "y" ]; then
	echo "creating autostart service... Name: VpnHoodServer";
	service="
[Unit]
Description=VpnHood Server
After=network.target

[Service]
Type=simple
ExecStart="$binDir/VpnHoodServer"
ExecStop="$binDir/VpnHoodServer" stop
TimeoutStartSec=0
Restart=always
RestartSec=10
StandardOutput=null

[Install]
WantedBy=default.target
";

	echo "$service" > "/etc/systemd/system/VpnHoodServer.service";

	echo "creating VpnHood Updater service... Name: VpnHoodUpdater";
	service="
[Unit]
Description=VpnHood Server Updater
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
	echo "$service" > "/etc/systemd/system/VpnHoodUpdater.service";

	# Executing services
	echo "Executing VpnHoodServer services...";
	systemctl daemon-reload;
	
	systemctl enable VpnHoodServer.service;
	systemctl restart VpnHoodServer.service;
	
	systemctl enable VpnHoodUpdater.service;
	systemctl restart VpnHoodUpdater.service;
fi
