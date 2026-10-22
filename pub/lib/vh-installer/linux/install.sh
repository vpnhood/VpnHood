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

function show_help() {
	echo "Usage: $assemblyName-linux.sh [options]";
	echo "  -q                    no questions: what is not given keeps the install's choices,";
	echo "                        and a new install starts at boot and updates itself";
	echo "  -noautostart          do not start the server at boot";
	echo "  -noautoupdate         do not update the server by itself";
	echo "  -httpBaseUrl <url>    the access server; with -httpAuthorization <value>";
	echo "  -managementSecret <secret>";
	echo "  -help                 this";
}

# Read arguments. The updaters of installed servers run this script too, with -q and -destination, and
# -container in a container.
autostart="";
autoupdate="";
for i;
do
arg=$i;
if [ "$arg" = "-noautostart" ]; then
	autostart="n";
	lastArg=""; continue;

elif [ "$arg" = "-noautoupdate" ]; then
	autoupdate="n";
	lastArg=""; continue;

elif [ "$arg" = "-q" ]; then
	quiet="y";
	lastArg=""; continue;

elif [ "$arg" = "-help" ] || [ "$arg" = "--help" ]; then
	show_help;
	exit 0;

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

# A choice no option made keeps the install's own, so an update - run with -q alone - changes none; a
# new install starts at boot and updates itself. Asked unless -q, with that as the answer Enter gives.
function unit_choice() {
	if [ ! -f "$destinationPath/publish.json" ] || systemctl is-enabled --quiet "$1" 2>/dev/null; then
		echo "y";
	else
		echo "n";
	fi
}
function ask() {
	local answer hint="Y/n";
	if [ "$2" = "n" ]; then hint="y/N"; fi
	read -r -p "$1 ($hint)? " answer;
	answer="${answer,,}";
	if [ "${answer:-$2}" = "y" ] || [ "${answer:0:1}" = "y" ]; then echo "y"; else echo "n"; fi
}
if [ "$container" != "y" ]; then
	defaultAutostart=$(unit_choice "$assemblyName.service");
	defaultAutoupdate=$(unit_choice "${assemblyName}Updater.service");
	if [ "$quiet" != "y" ]; then
		[ -z "$autostart" ] && autostart=$(ask "Start the server at boot" "$defaultAutostart");
		[ -z "$autoupdate" ] && autoupdate=$(ask "Keep the server up to date by itself" "$defaultAutoupdate");
	fi
	autostart=${autostart:-$defaultAutostart};
	autoupdate=${autoupdate:-$defaultAutoupdate};
fi

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

# ICU, once the package is in, as MsQuic below: .NET reads its culture data from it and stops at start
# without it. Most systems have it; a minimal one, Debian 13's cloud image among them, does not. Found
# by glob, as MsQuic is. Debian and Ubuntu carry ICU's version in the package's name (libicu76). Arch
# needs no branch: its pacman cannot run without ICU, so it is there wherever pacman is.
function icu_found() {
	local icuLib;
	for icuLib in /usr/lib64/libicuuc.so.* /usr/lib/libicuuc.so.* /usr/lib/*/libicuuc.so.*; do
		[ -e "$icuLib" ] && return 0;
	done
	return 1;
}
function install_icu() {
	local package;
	if command -v apt-get >/dev/null 2>&1; then
		DEBIAN_FRONTEND=noninteractive apt-get update -qq;
		package=$(apt-cache pkgnames libicu | grep -xE 'libicu[0-9]+(t64)?' | sort -V | tail -n 1);
		# waiting for the lock, which unattended-upgrades holds for minutes after a first boot
		[ -n "$package" ] && DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends \
			-o DPkg::Lock::Timeout=300 "$package";
	elif command -v dnf >/dev/null 2>&1; then
		dnf install -y -q libicu;
	elif command -v zypper >/dev/null 2>&1; then
		ZYPP_LOCK_TIMEOUT=300 zypper --non-interactive --quiet install libicu;
	fi
}
if ! icu_found; then
	echo "Installing ICU (libicu), which $productName needs...";
	install_icu;
	if ! icu_found; then
		echo "WARNING: Could not install ICU (libicu), without which $productName does not start.";
		echo "WARNING: Install it with your package manager.";
	fi
fi

# install ms-quic, once the package is in: a run that fails before changes nothing else. Where the
# distribution has no package for it, it goes in the version folder.
msquic_url="$releaseUrl/$assemblyName-linux-msquic.sh"
if ! msquic_script=$(wget -qO- "$msquic_url"); then
	echo "WARNING: Could not download MsQuic installer from: $msquic_url"
	echo "WARNING: wget failed. Skipping MsQuic installation."
elif ! bash <(printf '%s' "$msquic_script") "$binDir"; then
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
replace_file "$infoDir/vhuninstall" "$destinationPath/vhuninstall" 755;
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

# The units. The paths are quoted: the install's folder may hold a space. Starting at boot and updating
# are two choices: a unit neither ever asked for is not written, and one that is there stays, disabled,
# when its choice is no.
if [ "$container" != "y" ]; then
	serviceUnit="/etc/systemd/system/$assemblyName.service";
	updaterUnit="/etc/systemd/system/${assemblyName}Updater.service";

	if [ "$autostart" = "y" ] || [ -f "$serviceUnit" ]; then
		# network-online: at boot the server would otherwise start before there is a route, find no
		# primary adapter, and run without its tun (proxy only) until restarted
		echo "Writing the $assemblyName service...";
		service="
[Unit]
Description=$productName
Wants=network-online.target
After=network-online.target

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
		echo "$service" > "$serviceUnit";
	fi

	if [ "$autoupdate" = "y" ] || [ -f "$updaterUnit" ]; then
		# network-online: at boot the first check would otherwise run before there is a route, fail, and
		# wait 12 hours for the next
		echo "Writing the $assemblyName updater service...";
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
		echo "$service" > "$updaterUnit";
	fi

	systemctl daemon-reload;

	# A server that runs now runs the new version, whether or not it starts at boot
	if [ -f "$serviceUnit" ]; then
		if [ "$autostart" = "y" ]; then
			systemctl enable "$assemblyName.service";
		else
			systemctl disable "$assemblyName.service" 2>/dev/null;
		fi
		if [ "$autostart" = "y" ] || systemctl is-active --quiet "$assemblyName.service"; then
			systemctl restart "$assemblyName.service";
		fi
	fi

	# Never stopped from inside it: this script would stop with it and nothing would start it again.
	# Its next run takes the unit file and the vhupdate written here anyway.
	if [ -f "$updaterUnit" ]; then
		if [ "$autoupdate" = "y" ]; then
			systemctl enable "${assemblyName}Updater.service";
			if [ "$insideUpdaterUnit" != "y" ]; then
				systemctl restart "${assemblyName}Updater.service";
			fi
		else
			systemctl disable "${assemblyName}Updater.service" 2>/dev/null;
			if [ "$insideUpdaterUnit" != "y" ]; then
				systemctl stop "${assemblyName}Updater.service";
			fi
		fi
	fi
fi

# show final message
echo "$productName has been installed. Run the following command:";
echo "$destinationPath/$launcher";
if [ "$container" != "y" ]; then
	echo "To uninstall it: sudo \"$destinationPath/vhuninstall\"";
fi

# The firewall is the administrator's to open: this says only that there is one, as the ports are
# the server's settings, which can change after this.
firewall="";
if systemctl is-active --quiet firewalld 2>/dev/null; then
	firewall="firewalld";
elif command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null | grep -q "^Status: active"; then
	firewall="ufw";
fi
if [ "$container" != "y" ] && [ -n "$firewall" ]; then
	echo "NOTE: $firewall is active on this machine. Clients reach the server only through ports it";
	echo "      allows: allow the ports the server listens on (TCP 443 by default).";
fi
} # the group opened at the top
