#!/bin/bash
# Installation script for linux
read -p "Install .NET Runtime (y/n)?" install_net
read -p "Autorun (y/n)?" autorun

# variables
#installUrl="https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodServer.zip";
installUrl="https://github.com/vpnhood/VpnHood/releases/download/{tag}/VpnHoodServer.zip";
destinationPath="/opt/VpnHoodServer";

if [ "$install_net" = "y" ]; then
	wget https://packages.microsoft.com/config/ubuntu/20.10/packages-microsoft-prod.deb

	dpkg -i packages-microsoft-prod.deb
	apt-get update
	apt-get install -y apt-transport-https
	apt-get update
	apt-get install -y dotnet-runtime-5.0
	rm packages-microsoft-prod.deb
fi

# download & install VpnHoodServer
echo "Downloading VpnHoodServer...";
wget -O VpnHoodServer.zip $installUrl;

echo "Stop VpnHoodServer if exists...";
systemctl stop VpnHoodServer.service;

echo "Extracting to $destinationPath";
mkdir -p $destinationPath;
unzip -o VpnHoodServer.zip -d $destinationPath;
rm VpnHoodServer.zip


if [ "$autorun" = "y" ]; then
	echo "creating autostart service. Name: VpnHoodService...";
	service="
	[Unit]
	Description=VpnHood Server
	After=network.target

	[Service]
	Type=simple
	ExecStart=dotnet '/opt/VpnHoodServer/launcher/run.dll'
	TimeoutStartSec=0

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

