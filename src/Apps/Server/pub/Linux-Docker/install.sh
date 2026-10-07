#!/bin/bash
# One group, which bash reads whole before it runs any of it: a download cut short would otherwise
# run as far as it got.
{
echo "VpnHood Installation for linux";

# Default arguments
composeUrl="$composeUrlParam";
destinationPath="/opt/VpnHoodServer";
composeFile="VpnHoodServer.docker.yml";

# Read arguments
for i;
do
arg=$i;
if [ "$arg" = "-install-docker" ]; then
	installDocker="y";
	lastArg=""; continue;

elif [ "$arg" = "-q" ]; then
	quiet="y";
	lastArg=""; continue;

elif [ "$lastArg" = "-composeUrl" ]; then
	composeUrl=$arg;
	lastArg=""; continue;

elif [ "$lastArg" = "-composeFile" ]; then
	composeFile=$arg;
	lastArg=""; continue;

elif [ "$lastArg" = "-httpBaseUrl" ]; then
	httpBaseUrl=$arg;
	lastArg=""; continue;

elif [ "$lastArg" = "-httpAuthorization" ]; then
	httpAuthorization=$arg;
	lastArg=""; continue;

elif [ "$lastArg" = "-managementSecret" ]; then
	managementSecret=$arg;
	lastArg=""; continue;

elif [ "$lastArg" != "" ]; then
	echo "Unknown argument! argument: $lastArg";
	exit 1;
fi;
lastArg=$arg;
done;
# a last option still waiting for its value, or an unknown last one
if [ "$lastArg" != "" ]; then
	echo "Unknown argument or missing value! argument: $lastArg";
	exit 1;
fi

# Root, said here: Docker's install and the settings write to /etc and /opt.
if [ "$(id -u)" != "0" ]; then
	echo "This installer must run as root.";
	exit 1;
fi

# User interaction
if [ "$quiet" != "y" ] && [ "$installDocker" = "" ]; then
	read -p "Install Docker (y/n)?" installDocker;
fi;

# point to latest version if $installUrl is not set
if [ "$composeUrl" = "" ]; then
	composeUrl="https://github.com/vpnhood/VpnHood.App.Server/releases/latest/download/VpnHoodServer.docker.yml";
fi

# -----------------------------------------------
# Install Docker & Compose
# -----------------------------------------------
if [ "$installDocker" = "y" ] && docker compose version >/dev/null 2>&1; then
	echo "Docker and its compose plugin are installed already.";

elif [ "$installDocker" = "y" ]; then
	# Docker's repository for Ubuntu or Debian, which their derivatives take under the codename of the
	# release they are built on; on any other distro Docker is the operator's to install
	read -r distro codename <<< "$(. /etc/os-release;
		case " $ID $ID_LIKE " in
			*" ubuntu "*) echo "ubuntu ${UBUNTU_CODENAME:-$VERSION_CODENAME}";;
			*" debian "*) echo "debian $VERSION_CODENAME";;
		esac)";
	if [ -z "$codename" ]; then
		echo "Docker can be installed here only on Ubuntu, Debian and their derivatives. Install Docker, then run this without -install-docker.";
		exit 1;
	fi

	# wget: the compose file comes down with it
	apt-get update;
	apt-get install -y ca-certificates curl wget;

	# Docker's key; earlier installers wrote it as docker.gpg
	mkdir -p /etc/apt/keyrings;
	rm -f /etc/apt/keyrings/docker.gpg;
	if ! curl -fsSL "https://download.docker.com/linux/$distro/gpg" -o /etc/apt/keyrings/docker.asc; then
		echo "Could not download Docker's key.";
		exit 1;
	fi
	chmod a+r /etc/apt/keyrings/docker.asc;
	echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/$distro $codename stable" > /etc/apt/sources.list.d/docker.list;

	# A repository that fails is taken out again: left in, it would fail every apt-get update.
	if ! apt-get update || ! apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin; then
		echo "Could not install Docker from https://download.docker.com/linux/$distro ($codename). Install Docker, then run this without -install-docker.";
		rm -f /etc/apt/sources.list.d/docker.list;
		exit 1;
	fi
fi

# The compose file, through a temp file: a download that fails leaves the one in place alone.
echo "Downloading VpnHoodServer Docker Compose...";
composeTempFile=$(mktemp "$composeFile.XXXXXX") || exit 1;
if ! wget -nv -O "$composeTempFile" "$composeUrl"; then
	echo "Could not download $composeUrl";
	rm -f "$composeTempFile";
	exit 1;
fi
chmod 644 "$composeTempFile";
mv -f "$composeTempFile" "$composeFile" || exit 1;


# Write AppSettingss
if [ "$httpBaseUrl" != "" ]; then
	echo "creating the appsettings...";

	appSettings="{
  \"HttpAccessManager\": {
    \"BaseUrl\": \"$httpBaseUrl\",
    \"Authorization\": \"$httpAuthorization\"
  },
  \"ManagementSecret\": \"$managementSecret\"
}
";
	mkdir -p $destinationPath/storage;
	echo "$appSettings" > "$destinationPath/storage/appsettings.json";
fi

# Docker up, from the latest image. --remove-orphans removes what the compose file no longer names:
# the Watchtower container, VpnHoodUpdater, of earlier installs.
echo "Creating VpnHoodServer ...";
docker compose -p vpnhoodserver -f "$composeFile" up -d --pull always --remove-orphans || exit 1;

# up leaves a running server alone when neither its image nor its compose settings changed, so the
# settings written above reach it by a restart
if [ "$httpBaseUrl" != "" ]; then
	docker compose -p vpnhoodserver -f "$composeFile" restart;
fi
} # the group opened at the top
