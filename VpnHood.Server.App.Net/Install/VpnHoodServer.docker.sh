#!/bin/bash
echo "VpnHood Installation for linux";

# Default arguments
composeUrl="$composeUrlParam";
destinationPath="/opt/VpnHoodServer";
composeFile="VpnHoodServer.docker.yml";

# Read arguments
for i; 
do
if [ "$i" = "-install-docker" ]; then
	installDocker="y";
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

elif [ "$lastArg" != "" ]; then
	echo "Unknown argument! argument: $lastArg";
	exit;
fi;
lastArg=$i;
done;

# User interaction
if [ "$quiet" != "y" ]; then
	read -p "Install Docker (y/n)?" installDocker;
fi;

# point to latest version if $installUrl is not set
if [ "$composeUrl" = "" ]; then
	composeUrl="https://github.com/vpnhood/VpnHood/releases/latest/download/$composeFile";
fi

# install docker & compose
if [ "$installDocker" = "y" ]; then
	apt-get install -y docker.io;
	apt-get install -y docker-compose;
fi

# download & install VpnHoodServer
if [ "$packageFile" = "" ]; then
	echo "Downloading VpnHoodServer Docker Compose...";
	wget -O $composeFile $composeUrl;
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

# Docker up
echo "Creating VpnHoodServer ...";
docker-compose -p VpnHoodServer -f $composeFile up -d
