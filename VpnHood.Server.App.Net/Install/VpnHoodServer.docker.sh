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

elif [ "$lastArg" = "-composeFile" ]; then
	composeFile=$i;
	lastArg=""; continue;
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
	# Update the apt package index
	apt-get update;
	apt-get install -y ca-certificates curl gnupg lsb-release;

	# Add Docker’s official GPG key:
	mkdir -p /etc/apt/keyrings;
	rm -f /etc/apt/keyrings/docker.gpg;
	curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg;

	# set up the repository
	echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu \
  $(lsb_release -cs) stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null;

	#install Docker Engine
	apt-get update;
	apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin;
fi

# download & install VpnHoodServer
if [ "$packageFile" = "" ]; then
	echo "Downloading VpnHoodServer Docker Compose...";
	wget -O $composeFile $composeUrl;
fi


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
	mkdir -p $destinationPath/storage;
	echo "$appSettings" > "$destinationPath/appsettings.json";
fi

# Docker up
echo "Creating VpnHoodServer ...";
docker compose -p vpnhoodserver -f $composeFile up -d
