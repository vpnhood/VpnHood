#!/bin/bash
# One group, which bash reads whole before it runs any of it: the updater of earlier releases pipes
# this script into bash, and a download cut short would otherwise run as far as it got.
{
echo "$(productNameParam) Installation for linux";

# What this makes is everyone's to read and root's to write, whatever umask the caller brings
umask 022;

# Default arguments
releaseUrl="$(releaseUrlParam)";
packageUrl="$(packageUrlParam)";
versionTag="$(versionTagParam)";
assemblyName="$(assemblyNameParam)";
productName="$(productNameParam)";
launcher="$(launcherNameParam)";

# Calculated path
destinationPath="/opt/$assemblyName";
packageFile="";

# Read arguments
for i; 
do
arg=$i;
if [ "$arg" = "-autostart" ]; then
	autostart="y";
	lastArg=""; 
	continue;

elif [ "$arg" = "-q" ]; then
	quiet="y";
	lastArg=""; continue;

# Run in a container, by its supervisor: there is no systemd there, so no units either.
elif [ "$arg" = "-container" ]; then
	container="y";
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

elif [ "$lastArg" = "-packageUrl" ]; then
	packageUrl=$arg;
	lastArg=""; 
	continue;

elif [ "$lastArg" = "-packageFile" ]; then
	packageFile=$arg;
	lastArg="";
	continue;

# The install's folder; the updater passes its own, a container's /app.
elif [ "$lastArg" = "-destination" ]; then
	destinationPath=$arg;
	lastArg="";
	continue;

elif [ "$lastArg" = "-versionTag" ]; then
	versionTag=$arg;
	lastArg=""; 
	continue;


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

# Root, said here: everything below writes where only root may, and a run that finds that out one
# folder at a time leaves half an install behind.
if [ "$(id -u)" != "0" ]; then
	# Not $0, which names nothing to run again under bash <(...): /dev/fd/63. Nor sudo bash <(...):
	# sudo closes the descriptor that /dev/fd/63 is.
	echo "This installer must run as root. Try:";
	echo "  sudo su -c \"bash <(wget -qO- $releaseUrl/$assemblyName-linux.sh) $*\"";
	exit 1;
fi

# validate $versionTag
if [ "$versionTag" == "" ]; then
	echo "Could not find versionTag!";
	exit 1;
fi
binDir="$destinationPath/bin/$versionTag";

# Run by the updater, this script is inside the updater unit's own cgroup: vhupdate runs it inline,
# and restarting that unit from here would stop this script with it.
insideUpdaterUnit="n";
if grep -qF "/${assemblyName}Updater.service" /proc/self/cgroup 2>/dev/null; then
	insideUpdaterUnit="y";
fi

# User interaction
if [ "$quiet" != "y" ]; then
	if [ "$autostart" == "" ]; then
		read -p "Auto Start (Y/n)?" autostart;
		if [ -z "$autostart" ]; then
			autostart="y";
		fi;
	fi;
fi;

# In a container nothing else patches the libraries the server runs on but never installs itself -
# the image brought them - so each release upgrades them there: OpenSSL, the root certificates, ICU,
# zlib and glibc, the installed ones found by pattern since their package names carry versions.
# Best effort: a failure is logged and the server is installed anyway.
if [ "$container" = "y" ]; then
	echo "Upgrading the libraries the server runs on...";
	# dpkg first: a container stopped during an earlier upgrade has left it interrupted, and apt does
	# nothing until dpkg has finished that
	DEBIAN_FRONTEND=noninteractive dpkg --force-confdef --force-confold --configure -a;
	libraries=$(dpkg-query -W -f='${Status} ${Package}\n' 'libssl*' openssl ca-certificates 'libicu*' zlib1g libc6 2>/dev/null |
		awk '$3 == "installed" { print $4 }');
	if [ -z "$libraries" ] || ! DEBIAN_FRONTEND=noninteractive apt-get update -qq ||
		! DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --only-upgrade \
			-o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold $libraries; then
		echo "WARNING: Could not upgrade the libraries the server runs on. Installing the server anyway.";
	fi
fi

# download & install the module. Into the install's own folder, not the current one: the updater's
# is /, where every update used to leave the package. A crashed run's leftovers go first. 755 even
# where an earlier run under a stricter umask made them.
mkdir -p "$destinationPath/bin" || exit 1;
chmod 755 "$destinationPath" "$destinationPath/bin";
rm -rf "$destinationPath"/bin/.package.* "$destinationPath"/bin/.staging.*;

# A version already in bin is complete and is kept, a running server's files with it, and nothing is
# downloaded for it. Another is extracted into a staging folder that is renamed into bin whole, so a
# version in bin is never half there.
if [ -d "$binDir" ]; then
	echo "Already installed: $binDir";
else
	downloadedPackageFile="";
	if [ "$packageFile" = "" ]; then
		echo "Downloading $productName...";
		packageFile=$(mktemp "$destinationPath/bin/.package.XXXXXX") || exit 1;
		downloadedPackageFile="$packageFile";
		if ! wget -nv -O "$packageFile" "$packageUrl"; then
			echo "Could not download $packageUrl";
			rm -f "$downloadedPackageFile";
			exit 1;
		fi
	fi

	echo "Extracting to $binDir";
	stagingDir=$(mktemp -d "$destinationPath/bin/.staging.XXXXXX") || exit 1;
	# Root's files with plain modes: tar run by root keeps the owner the package records, the build
	# machine's user id, which here may be a person's, who could then replace what the root service
	# runs; and a package made on Windows records 0666 and 0777. The folder must be this version's, or
	# the rename would put something else in its place. touch: tar keeps the build's times, and the
	# cleanup below goes by when a version was installed.
	if ! tar --no-same-owner --no-same-permissions -xzf "$packageFile" -C "$stagingDir" ||
		[ ! -d "$stagingDir/$versionTag" ] || ! chmod -R u=rwX,go=rX "$stagingDir/$versionTag" ||
		! touch "$stagingDir/$versionTag" || ! mv "$stagingDir/$versionTag" "$binDir"; then
		echo "Could not extract $versionTag from $packageFile";
		rm -rf "$stagingDir" "$downloadedPackageFile";
		exit 1;
	fi
	rm -rf "$stagingDir" "$downloadedPackageFile";
fi
# the package the updater of earlier releases left in its working folder, /
rm -f "/$assemblyName-linux.tar.gz";

# install ms-quic, once the package is in: a run that fails before changes nothing else
msquic_url="$releaseUrl/$assemblyName-linux-msquic.sh"
if ! msquic_script=$(wget -qO- "$msquic_url"); then
	echo "WARNING: Could not download MsQuic installer from: $msquic_url"
	echo "WARNING: wget failed. Skipping MsQuic installation."
elif ! bash <(printf '%s' "$msquic_script"); then
	echo "WARNING: MsQuic installation failed. Skipping."
fi

# The shared files are replaced by rename, never rewritten in place: the vhupdate running this script
# is still reading its own file, as bash reads a script as it goes. publish.json, which switches the
# version, goes last.
echo "Updating shared files...";
infoDir="$binDir/publish_info";
function replace_file() {
	local source="$1";
	local target="$2";
	local mode="$3";
	if ! cp -f "$source" "$target.new" || ! chmod "$mode" "$target.new" || ! mv -f "$target.new" "$target"; then
		echo "Could not update $target";
		exit 1;
	fi
}
chmod +x "$binDir/$assemblyName";
replace_file "$infoDir/vhupdate" "$destinationPath/vhupdate" 755;
replace_file "$infoDir/$launcher" "$destinationPath/$launcher" 755;
replace_file "$infoDir/publish.json" "$destinationPath/publish.json" 644;

# Old versions go only here, at an update: one stays while it is the version this run installs, among
# the three newest, was installed in the last 30 days or a process still runs from it. Version folders
# at the top, from the layout before bin, count too; a folder whose name is not a whole version is
# left alone.
function remove_old_versions() {
	local versionDirs=() versionDir newest running versionPattern='^v[0-9]+(\.[0-9]+){1,3}(-.+)?$';
	local installing="$(readlink -f "$binDir")";
	for versionDir in "$destinationPath"/bin/v[0-9]* "$destinationPath"/v[0-9]*; do
		if [ -d "$versionDir" ] && [[ $(basename "$versionDir") =~ $versionPattern ]]; then
			versionDirs+=("$(readlink -f "$versionDir")");
		fi
	done
	newest=$(for versionDir in "${versionDirs[@]}"; do basename "$versionDir"; done | sort -V -r | head -3);
	running=$(readlink /proc/[0-9]*/exe 2>/dev/null);
	for versionDir in "${versionDirs[@]}"; do
		if [ "$versionDir" = "$installing" ] || grep -qxF "$(basename "$versionDir")" <<< "$newest" ||
			[ -n "$(find "$versionDir" -maxdepth 0 -mtime -30)" ] || grep -qF "$versionDir/" <<< "$running"; then
			continue;
		fi
		echo "Removing an old version: $versionDir";
		rm -rf "$versionDir" || echo "WARNING: Could not remove $versionDir";
	done
}
remove_old_versions;

# The storage, root's alone: the settings below carry the management secret, and the server keeps its
# certificates beside them. Not in a container, which has no other users and whose storage may be a
# folder of the host's.
storagePath="$destinationPath/storage";
if [ "$container" != "y" ]; then
	mkdir -p "$storagePath" && chmod 700 "$storagePath";
fi

# A value as a JSON string: backslashes and quotes escaped, control characters dropped.
function json_string() {
	printf '"%s"' "$(printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' | tr -d '[:cntrl:]')";
}

# Write AppSettings, through a new file: a reader that opened the old one while the folder was open
# to all never sees the new secret.
if [ "$httpBaseUrl" != "" ]; then
	appSettings="{
  \"HttpAccessManager\": {
    \"BaseUrl\": $(json_string "$httpBaseUrl"),
    \"Authorization\": $(json_string "$httpAuthorization")
  },
  \"ManagementSecret\": $(json_string "$managementSecret")
}
";
	if ! mkdir -p "$storagePath" || ! echo "$appSettings" > "$storagePath/appsettings.json.new" ||
		! chmod 600 "$storagePath/appsettings.json.new" ||
		! mv -f "$storagePath/appsettings.json.new" "$storagePath/appsettings.json"; then
		echo "Could not write $storagePath/appsettings.json";
		exit 1;
	fi
fi

# init service. The paths are quoted: the install's folder may hold a space.
if [ "${autostart,,}" = "y" ] && [ "$container" != "y" ]; then
	echo "creating autostart service... Name: $assemblyName";
	service="
[Unit]
Description=$productName
After=network.target

[Service]
Type=simple
ExecStart=\"$destinationPath/$launcher\"
ExecStop=\"$destinationPath/$launcher\" stop
TimeoutStartSec=0
Restart=on-failure
RestartSec=10
StandardOutput=null
StandardError=journal

[Install]
WantedBy=default.target
";

	echo "$service" > "/etc/systemd/system/$assemblyName.service";

	# network-online: at boot the first check would otherwise run before there is a route, fail, and
	# wait 12 hours for the next
	echo "creating VpnHood Updater service... Name: ${assemblyName}Updater";
	service="
[Unit]
Description=$productName Updater
Wants=network-online.target
After=network-online.target

[Service]
Type=simple
ExecStart=\"$destinationPath/vhupdate\"
TimeoutStartSec=0
Restart=always
RestartSec=720min

[Install]
WantedBy=default.target
";
	echo "$service" > "/etc/systemd/system/${assemblyName}Updater.service";

	# Executing services
	echo "Executing $assemblyName services...";
	systemctl daemon-reload;
	
	systemctl enable $assemblyName.service;
	systemctl restart $assemblyName.service;
	
	systemctl enable ${assemblyName}Updater.service;
	# Not from inside it (above): the unit's next run takes the unit file and the vhupdate written
	# here anyway.
	if [ "$insideUpdaterUnit" != "y" ]; then
		systemctl restart ${assemblyName}Updater.service;
	fi
fi

# show final message
echo "$productName has been installed. Run the following command:";
echo "$destinationPath/$launcher";
} # the group opened at the top
