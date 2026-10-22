#!/bin/bash
# One group, which bash reads whole before it runs any of it: a download cut short would otherwise
# run as far as it got.
{
echo "VpnHood Installation for linux";

# Default arguments
composeUrl="$composeUrlParam";
destinationPath="/opt/VpnHoodServer";
composeFile="VpnHoodServer.docker.yml";

# The options an operator chooses. -composeUrl and -composeFile, where the release's compose file comes
# from and goes, are left out, as the native installer leaves out its package's.
function show_help() {
	echo "Usage: VpnHoodServer.docker.sh [options]";
	echo "  -q                    no questions: Docker is installed only with -install-docker";
	echo "  -install-docker       install Docker and its compose plugin where they are missing,";
	echo "                        on Ubuntu, Debian and their derivatives";
	echo "  -httpBaseUrl <url>    the access server; with -httpAuthorization <value>";
	echo "  -managementSecret <secret>";
	echo "  -h, -help             this";
	echo "Options take one dash or two: -install-docker or --install-docker.";
}

# Read arguments
for i;
do
arg=$i;
# An option takes two dashes too, as the GNU tools' long options do; a value is left as it is.
if [ "$lastArg" = "" ] && [ "${arg:0:2}" = "--" ]; then
	arg="${arg:1}";
fi

if [ "$arg" = "-install-docker" ]; then
	installDocker="y";
	lastArg=""; continue;

elif [ "$arg" = "-q" ]; then
	quiet="y";
	lastArg=""; continue;

elif [ "$arg" = "-help" ] || [ "$arg" = "-h" ]; then
	show_help;
	exit 0;

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
	echo "Unknown argument! argument: $lastArg (-help lists the options)";
	exit 1;
fi;
lastArg=$arg;
done;
# a last option still waiting for its value, or an unknown last one
if [ "$lastArg" != "" ]; then
	echo "Unknown argument or missing value! argument: $lastArg (-help lists the options)";
	exit 1;
fi

# Root, said here: Docker's install and the settings write to /etc and /opt.
if [ "$(id -u)" != "0" ]; then
	echo "This installer must run as root.";
	exit 1;
fi

# A question, as the native installer asks it: Enter gives the answer in capitals, y or yes in any case
# is yes, and anything else no.
function ask() {
	local answer hint="Y/n";
	if [ "$2" = "n" ]; then hint="y/N"; fi
	read -r -p "$1 ($hint)? " answer;
	answer="${answer,,}";
	if [ "${answer:-$2}" = "y" ] || [ "${answer:0:1}" = "y" ]; then echo "y"; else echo "n"; fi
}

# point to latest version if $installUrl is not set
if [ "$composeUrl" = "" ]; then
	composeUrl="https://github.com/vpnhood/VpnHood.App.Server/releases/latest/download/VpnHoodServer.docker.yml";
fi

# -----------------------------------------------
# Install Docker & Compose
# -----------------------------------------------
# Everything below needs both. Asked only when they are missing, with Enter for yes, as there is no
# going on without them; -q asks nothing, so a script installs them only with -install-docker. Without
# them this stops here, rather than at the container's start.
dockerFound="n";
if docker compose version >/dev/null 2>&1; then
	dockerFound="y";
elif [ "$installDocker" != "y" ] && [ "$quiet" != "y" ]; then
	installDocker=$(ask "Docker and its compose plugin are not installed. Install them" "y");
fi

if [ "$dockerFound" = "y" ]; then
	if [ "$installDocker" = "y" ]; then
		echo "Docker and its compose plugin are installed already.";
	fi

elif [ "$installDocker" != "y" ]; then
	echo "VpnHoodServer runs in Docker. Install Docker and its compose plugin, then run this again, or run it with -install-docker.";
	exit 1;

else
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


# The storage, root's alone, as a native install's: the settings carry the management secret, and the
# server keeps its certificates and tokens beside them. Made before the container is, as Docker would
# make the mount's folder open to all.
storagePath="$destinationPath/storage";
if ! mkdir -p "$storagePath" || ! chmod 700 "$storagePath"; then
	echo "Could not make $storagePath";
	exit 1;
fi

# A value as a JSON string: backslashes and quotes escaped, control characters dropped.
function json_string() {
	printf '"%s"' "$(printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' | tr -d '[:cntrl:]')";
}

# Write AppSettings, through a new file that only root reads
if [ "$httpBaseUrl" != "" ]; then
	echo "creating the appsettings...";

	appSettings="{
  \"HttpAccessManager\": {
    \"BaseUrl\": $(json_string "$httpBaseUrl"),
    \"Authorization\": $(json_string "$httpAuthorization")
  },
  \"ManagementSecret\": $(json_string "$managementSecret")
}
";
	if ! echo "$appSettings" > "$storagePath/appsettings.json.new" ||
		! chmod 600 "$storagePath/appsettings.json.new" ||
		! mv -f "$storagePath/appsettings.json.new" "$storagePath/appsettings.json"; then
		echo "Could not write $storagePath/appsettings.json";
		exit 1;
	fi
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
