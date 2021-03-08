#!/bin/bash
serviceName="VpnHood.AccessServer";
zipFileName="package.zip";

# stop VpnHood.AccessServer.service if exists
systemctl stop $serviceName.service;

#Copy config file
destinationPath="/opt/$serviceName";
mkdir -p "$destinationPath";
cp appsettings.json "$destinationPath/"

#install vpnhood
echo "Extracting to $destinationPath";
unzip -o $zipFileName -d $destinationPath;

#Create service
echo "creating autostart service. Name: $serviceName...";
service="
[Unit]
Description=
After=network.target

[Service]
Type=simple
ExecStart=dotnet '/opt/$serviceName/launcher/run.dll'
TimeoutStartSec=0

[Install]
WantedBy=default.target
"
echo "$service" > "/etc/systemd/system/$serviceName.service";

# run service
echo "run $serviceName service...";
systemctl daemon-reload;
systemctl enable $serviceName;
systemctl start $serviceName;
