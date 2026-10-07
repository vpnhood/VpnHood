#!/bin/bash
echo "$(productNameParam) Installation for linux";

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

# validate $versionTag
if [ "$versionTag" == "" ]; then
	echo "Could not find versionTag!";
	exit 1;
fi
binDir="$destinationPath/bin/$versionTag";

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
	libraries=$(dpkg-query -W -f='${Status} ${Package}\n' 'libssl*' openssl ca-certificates 'libicu*' zlib1g libc6 2>/dev/null |
		awk '$3 == "installed" { print $4 }');
	if [ -z "$libraries" ] || ! DEBIAN_FRONTEND=noninteractive apt-get update -qq ||
		! DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --only-upgrade \
			-o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold $libraries; then
		echo "WARNING: Could not upgrade the libraries the server runs on. Installing the server anyway.";
	fi
fi

# install ms-quic
msquic_url="$releaseUrl/VpnHoodServer-linux-msquic.sh"
if ! msquic_script=$(wget -qO- "$msquic_url"); then
	echo "WARNING: Could not download MsQuic installer from: $msquic_url"
	echo "WARNING: wget failed. Skipping MsQuic installation."
elif ! bash <(printf '%s' "$msquic_script"); then
	echo "WARNING: MsQuic installation failed. Skipping."
fi

# download & install the module. Into the install's own folder, not the current one: the updater's
# is /, where every update used to leave the package. A crashed run's leftovers go first.
mkdir -p "$destinationPath/bin" || exit 1;
rm -rf "$destinationPath"/bin/.package.* "$destinationPath"/bin/.staging.*;
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

# extract into a staging folder that is renamed into bin whole, so a version in bin is never half
# there; one already there is complete and is kept, a running server's files with it
if [ -d "$binDir" ]; then
	echo "Already installed: $binDir";
else
	echo "Extracting to $binDir";
	stagingDir=$(mktemp -d "$destinationPath/bin/.staging.XXXXXX") || exit 1;
	# touch: tar keeps the build's times, and the cleanup below goes by when a version was installed
	if ! tar -xzf "$packageFile" -C "$stagingDir" || ! touch "$stagingDir/$versionTag" ||
		! mv "$stagingDir/$versionTag" "$binDir"; then
		echo "Could not extract $packageFile";
		rm -rf "$stagingDir" "$downloadedPackageFile";
		exit 1;
	fi
	rm -rf "$stagingDir";
fi
rm -f "$downloadedPackageFile";

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

# Old versions go only here, at an update: one stays while it is among the three newest or was
# installed in the last 30 days. Version folders at the top, from the layout before bin, count too.
function remove_old_versions() {
	local versionDirs=() versionDir newest;
	for versionDir in "$destinationPath"/bin/v[0-9]* "$destinationPath"/v[0-9]*; do
		if [ -d "$versionDir" ]; then versionDirs+=("$versionDir"); fi
	done
	newest=$(for versionDir in "${versionDirs[@]}"; do basename "$versionDir"; done | sort -V -r | head -3);
	for versionDir in "${versionDirs[@]}"; do
		if grep -qxF "$(basename "$versionDir")" <<< "$newest" || [ -n "$(find "$versionDir" -maxdepth 0 -mtime -30)" ]; then
			continue;
		fi
		echo "Removing an old version: $versionDir";
		rm -rf "$versionDir" || echo "WARNING: Could not remove $versionDir";
	done
}
remove_old_versions;

# Write AppSettingss
if [ "$httpBaseUrl" != "" ]; then
	appSettings="{
  \"HttpAccessManager\": {
    \"BaseUrl\": \"$httpBaseUrl\",
    \"Authorization\": \"$httpAuthorization\"
  },
  \"ManagementSecret\": \"$managementSecret\"
}
";
	mkdir -p "$destinationPath/storage";
	echo "$appSettings" > "$destinationPath/storage/appsettings.json"
fi

# init service
if [ "${autostart,,}" = "y" ] && [ "$container" != "y" ]; then
	echo "creating autostart service... Name: $assemblyName";
	service="
[Unit]
Description=$productName
After=network.target

[Service]
Type=simple
ExecStart="$destinationPath/$launcher"
ExecStop="$destinationPath/$launcher" stop
TimeoutStartSec=0
Restart=on-failure
RestartSec=10
StandardOutput=null
StandardError=journal

[Install]
WantedBy=default.target
";

	echo "$service" > "/etc/systemd/system/$assemblyName.service";

	echo "creating VpnHood Updater service... Name: ${assemblyName}Updater";
	service="
[Unit]
Description=$productName Updater
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
	echo "$service" > "/etc/systemd/system/${assemblyName}Updater.service";

	# Executing services
	echo "Executing $assemblyName services...";
	systemctl daemon-reload;
	
	systemctl enable $assemblyName.service;
	systemctl restart $assemblyName.service;
	
	systemctl enable ${assemblyName}Updater.service;
	systemctl restart ${assemblyName}Updater.service;
fi

# show final message
echo "$productName has been installed. Run the following command:";
echo "$destinationPath/$launcher";
