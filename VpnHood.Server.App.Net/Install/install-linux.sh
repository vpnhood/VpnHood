#!/bin/bash
packageFile=$1;

echo "Installation script for linux";
read -p "Install .NET Runtime 5.0 for ubuntu/21.04 (y/n)?" install_net;
netUrl="https://packages.microsoft.com/config/ubuntu/21.04/packages-microsoft-prod.deb"
read -p "Auto Start (y/n)?" autostart;

# Variables
installUrl="$installUrlParam";
destinationPath="/opt/VpnHoodServer";

# point to latest version if $installUrl is not et
if [ "$installUrl" = "" ]; then
	installUrl="https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodServer.zip";
fi

# install dotnet for Ubunto
if [ "$install_net" = "y" ]; then
	wget $netUrl -O packages-microsoft-prod.deb
	dpkg -i packages-microsoft-prod.deb
	rm packages-microsoft-prod.deb

	apt-get update
	apt-get install -y apt-transport-https
	apt-get update
	apt-get install -y dotnet-runtime-5.0
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
ExecStart=sh -c \"dotnet '/opt/VpnHoodServer/launcher/run.dll' -launcher:noLaunchAfterUpdate && sleep 10s\"
TimeoutStartSec=0
Restart=always
RestartSec=2

[Install]
WantedBy=default.target
"
	echo "$service" > "/etc/systemd/system/VpnHoodServer.service";


	# run service
	echo "run VpnHoodServer service...";
	systemctl daemon-reload;
	systemctl enable VpnHoodServer.service;
	systemctl start VpnHoodServer.service;
fi
