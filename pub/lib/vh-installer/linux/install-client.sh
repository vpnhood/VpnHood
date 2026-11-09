#!/bin/bash
# The desktop client's installer. The server's install.sh is next to this one and is NOT this:
# a server is a daemon and nothing else, while a client is three things out of one binary - a
# root service that owns the tunnel, a window that a logged-in person opens, and commands they
# type - and the difference is almost entirely in what gets installed around the binary.
#
# What this writes:
#   /opt/<name>/bin/<version>/   the self-contained build, the old ones removed at an update
#   /opt/<name>/storage/         settings.json, profiles, the log  (root's alone, 700)
#   /opt/<name>/kill-switch-off  the kill switch's way out, which needs neither the service nor the app
#   /usr/local/bin/<launcher>    so the commands can be typed from anywhere
#   /etc/systemd/system/<name>.service           the root service, headless
#   /etc/systemd/system/<name>Updater.service    the updater, vhupdate, run by its timer
#   /etc/systemd/system/<name>Updater.timer      at boot and every 12 hours, while updates are on
#   /usr/share/applications/<name>.desktop       the window, in the app grid
#   /etc/xdg/autostart/<name>.desktop            the tray, at every sign-in
#   /usr/share/icons/hicolor/256x256/apps/<name>.png   its icon
#
# Ubuntu and Debian are what it is tested on; anything with systemd and apt, or systemd and the
# GUI libraries already present, will do.

# One group, which bash reads whole before it runs any of it: the updater of earlier releases pipes
# this script into bash, and a download cut short would otherwise run as far as it got.
{
echo "$(productNameParam) Installation for Linux";

# What this makes is everyone's to read and root's to write, whatever umask the caller brings: under a
# person's 077 the window and the commands, which run as that person, could not reach the install.
umask 022;

# Default arguments
releaseUrl="$(releaseUrlParam)";
packageUrl="$(packageUrlParam)";
versionTag="$(versionTagParam)";
assemblyName="$(assemblyNameParam)";
productName="$(productNameParam)";
launcher="$(launcherNameParam)";
vpnHostName="$(vpnHostNameParam)";
packageCpu="$(cpuParam)";

# Calculated path
destinationPath="/opt/$assemblyName";
packageFile="";
autostart="";
autoupdate="";
quiet="";
withDesktop="";

function show_help() {
	echo "Usage: $assemblyName-linux.sh [options]";
	echo "  -q                    no questions: what is not given keeps the install's choices,";
	echo "                        and a new install starts at boot and updates itself";
	echo "  -noautostart          do not start the VPN service at boot";
	echo "  -noautoupdate         do not update the app by itself";
	echo "  -autoupdate           update it by itself again, after -noautoupdate";
	echo "  -desktop, -nodesktop  the menu entry and the window's libraries, or not (found by default)";
	echo "  -h, -help             this";
	echo "Options take one dash or two: -noautoupdate or --noautoupdate.";
}

# Read arguments. Every option an earlier release took still works: the updaters of installed apps
# run this script with theirs.
for i;
do
arg=$i;
# An option takes two dashes too, as the GNU tools' long options do; a value is left as it is.
if [ "$lastArg" = "" ] && [ "${arg:0:2}" = "--" ]; then
	arg="${arg:1}";
fi

# Compatibility (2026-10): the default now; the updaters before this release pass it. Drop a few months
# after this ships.
if [ "$arg" = "-autostart" ]; then
	autostart="y";
	lastArg="";
	continue;

elif [ "$arg" = "-noautostart" ]; then
	autostart="n";
	lastArg=""; continue;

elif [ "$arg" = "-autoupdate" ]; then
	autoupdate="y";
	lastArg=""; continue;

elif [ "$arg" = "-noautoupdate" ]; then
	autoupdate="n";
	lastArg=""; continue;

elif [ "$arg" = "-q" ]; then
	quiet="y";
	lastArg=""; continue;

elif [ "$arg" = "-help" ] || [ "$arg" = "-h" ]; then
	show_help;
	exit 0;

# Force the window's side of the install on or off rather than let it be guessed. -nodesktop is
# for a server or a container: no desktop entry, no GUI libraries pulled in, the service and the
# commands only.
elif [ "$arg" = "-desktop" ]; then
	withDesktop="y";
	lastArg=""; continue;

elif [ "$arg" = "-nodesktop" ]; then
	withDesktop="n";
	lastArg=""; continue;

elif [ "$lastArg" = "-packageUrl" ]; then
	packageUrl=$arg;
	lastArg="";
	continue;

elif [ "$lastArg" = "-packageFile" ]; then
	packageFile=$arg;
	lastArg="";
	continue;

# The install's folder; the updater passes its own.
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

# The build needs glibc. On musl, Alpine's, neither the service nor the commands can start, with a desktop
# or without, so nothing goes in. By what ldd says: a machine that cannot say is not refused.
if ldd --version 2>&1 | grep -qi musl; then
	echo "$productName cannot run on this system: it needs glibc, and this system uses musl, as Alpine does.";
	exit 1;
fi

# The package is one CPU's, and another's cannot start here, so nothing goes in. The any-CPU script chooses by the
# same test, so only a script downloaded by hand comes here with the wrong one. A 32-bit userland on a 64-bit kernel,
# as Raspberry Pi OS 32-bit has, says aarch64 too, yet runs no arm64 build.
machineCpu=$(uname -m);
if [ "$(getconf LONG_BIT 2>/dev/null)" = "32" ]; then
	machineCpu="$machineCpu with a 32-bit userland";
fi
case "$machineCpu" in
	x86_64) machineCpu="x64";;
	aarch64) machineCpu="arm64";;
	*)
		echo "$productName cannot run on this machine: it needs x64 or arm64, and this one is $machineCpu.";
		exit 1;;
esac
if [ "$machineCpu" != "$packageCpu" ]; then
	echo "This installer is for $packageCpu, and this machine is $machineCpu, so nothing was installed.";
	echo "Download the $machineCpu one: $releaseUrl/$assemblyName-linux-$machineCpu.sh";
	echo "Or run this command, which picks it for you:";
	echo "  sudo bash -c 'u=$releaseUrl/$assemblyName-linux.sh; bash <(wget -qO- \$u 2>/dev/null || curl -fsSL \$u) $*'";
	exit 1;
fi

# The x64 build's .NET needs SSE3, SSSE3, SSE4.1, SSE4.2 and POPCNT (x86-64-v2), and without them stops as the app
# starts, before any of its code runs: an old processor, or a virtual machine's basic one, which Linux names "Common
# KVM processor", lacks them. So nothing goes in, and an installed version keeps running. By the flags the kernel
# lists: a machine that cannot list them is not refused.
cpuFlags=$(grep -m1 '^flags' /proc/cpuinfo 2>/dev/null);
if [ "$machineCpu" = "x64" ] && [ -n "$cpuFlags" ]; then
	for flag in pni ssse3 sse4_1 sse4_2 popcnt; do
		if ! grep -qw "$flag" <<< "$cpuFlags"; then
			echo "$productName cannot run on this machine: it needs a processor with SSE4.2 and POPCNT, and this one does not have them.";
			exit 1;
		fi
	done
fi

# Root, said here. Everything below writes to /opt, /etc and /usr, and a run that discovers that
# one directory at a time leaves half an install behind.
if [ "$(id -u)" != "0" ]; then
	# Not $0, which names nothing to run again: /dev/fd/63 under bash <(...), "--" under the any-CPU
	# script. Nor sudo bash <(...): sudo closes the descriptor that /dev/fd/63 is.
	echo "This installer must run as root. Try:";
	echo "  sudo bash -c 'u=$releaseUrl/$assemblyName-linux.sh; bash <(wget -qO- \$u 2>/dev/null || curl -fsSL \$u) $*'";
	exit 1;
fi

# validate $versionTag
if [ "$versionTag" == "" ]; then
	echo "Could not find versionTag!";
	exit 1;
fi
binDir="$destinationPath/bin/$versionTag";

# Run by the updater, this script is inside the updater unit's own cgroup: the old vhupdate runs it
# inline. Restarting that unit from here would stop this script with it, before the VPN service is
# started again below, and leave the machine with no VPN until someone notices.
insideUpdaterUnit="n";
if grep -qF "/${assemblyName}Updater.service" /proc/self/cgroup 2>/dev/null; then
	insideUpdaterUnit="y";
fi

# Is there a desktop on this machine? Only one with a display manager, or WSLg, which shows Linux
# windows on Windows with none, gets the window, its icon and its libraries; a server install stops
# at the service and the commands. Not the default target, which a server reports as
# graphical.target too (Ubuntu Server, the cloud images). Asked before the prompts so the answer can
# be shown rather than demanded.
if [ "$withDesktop" == "" ]; then
	if systemctl is-enabled --quiet display-manager.service 2>/dev/null || [ -d /mnt/wslg ]; then
		withDesktop="y";
	else
		withDesktop="n";
	fi
fi

# A choice no option made keeps the install's own, so an update - run with -q alone - changes none; a
# new install starts at boot and updates itself. The boot is asked unless -q, with that as the answer
# Enter gives; updating never is, as on Windows: -noautoupdate alone turns it off, -autoupdate on again.
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
defaultAutostart=$(unit_choice "$assemblyName.service");
# the timer, or the updater unit itself in an install from before it, which restarted every 12 hours
updaterChoiceUnit="${assemblyName}Updater.timer";
if [ ! -f "/etc/systemd/system/$updaterChoiceUnit" ]; then
	updaterChoiceUnit="${assemblyName}Updater.service";
fi
defaultAutoupdate=$(unit_choice "$updaterChoiceUnit");
if [ "$quiet" != "y" ]; then
	[ -z "$autostart" ] && autostart=$(ask "Start the VpnHood service at boot" "$defaultAutostart");
fi
autostart=${autostart:-$defaultAutostart};
autoupdate=${autoupdate:-$defaultAutoupdate};

# One install at a time from here, where this run starts to change things. Beside another, the one
# that ends last has the last word - an update ending after a run with -noautoupdate turns updates
# back on - and each deletes the other's download as a crashed run's leftovers. So the second waits;
# the lock goes when its run ends, even by a crash. After the questions, so one left unanswered holds
# up no update.
if command -v flock >/dev/null 2>&1; then
	exec 9>>"/run/$assemblyName-install.lock";
	if ! flock -n 9; then
		echo "Another install or uninstall of $productName is running. Waiting for it to finish...";
		flock 9;
	fi
fi

# ------------------------------------------------------------------
# Dependencies
# ------------------------------------------------------------------
# The build is self-contained - it carries .NET and Skia - so what a bare machine lacks is ICU,
# which .NET needs (once the package is in, below), and the C libraries Avalonia opens by name at
# runtime, needed only where there is a window to draw. A failure here is a warning, not an exit:
# a machine with them already, or one that is not Debian at all, must still finish installing.
if [ "$withDesktop" == "y" ] && command -v dpkg >/dev/null 2>&1 && command -v apt-get >/dev/null 2>&1; then
	missing="";
	for lib in libx11-6 libice6 libsm6 libfontconfig1 xdg-utils; do
		if ! dpkg -s "$lib" >/dev/null 2>&1; then
			missing="$missing $lib";
		fi
	done

	if [ -n "$missing" ]; then
		echo "Installing the libraries the window needs:$missing";
		DEBIAN_FRONTEND=noninteractive apt-get update -qq;
		# --no-install-recommends, and it matters: xdg-utils RECOMMENDS a terminal emulator and
		# an X utility set, so without this a VPN client quietly installs tilix, x11-utils,
		# cpp and libllvm on somebody's machine. Only what the window actually opens by name.
		# shellcheck disable=SC2086
		if ! DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends $missing; then
			echo "WARNING: Could not install:$missing";
			echo "WARNING: The commands and the service will work; the window may not open.";
		fi
	fi

# Not Debian: the files themselves - the libraries as the loader finds them, and xdg-open - as each family
# names its packages differently. What is missing comes in by the family's manager, without what its packages
# only recommend, as above, and a warning names what still is.
elif [ "$withDesktop" == "y" ]; then
	function window_files_missing() {
		local lib loaderLibs;
		loaderLibs=$(ldconfig -p 2>/dev/null | awk '{ print $1 }');
		for lib in libX11.so.6 libICE.so.6 libSM.so.6 libfontconfig.so.1; do
			if ! grep -qxF "$lib" <<< "$loaderLibs"; then
				printf ' %s' "$lib";
			fi
		done
		if ! command -v xdg-open >/dev/null 2>&1; then
			printf ' xdg-open';
		fi
	}
	function install_window_files() {
		if command -v dnf >/dev/null 2>&1; then
			dnf install -y -q --setopt=install_weak_deps=False libX11 libICE libSM fontconfig xdg-utils;
		elif command -v zypper >/dev/null 2>&1; then
			ZYPP_LOCK_TIMEOUT=300 zypper --non-interactive --quiet install --no-recommends \
				libX11-6 libICE6 libSM6 libfontconfig1 xdg-utils;
		elif command -v pacman >/dev/null 2>&1; then
			pacman -S --noconfirm --needed libx11 libice libsm fontconfig xdg-utils;
		fi
	}

	missing=$(window_files_missing);
	if [ -n "$missing" ]; then
		echo "Installing the libraries the window needs:$missing";
		install_window_files;
		ldconfig 2>/dev/null;
		missing=$(window_files_missing);
	fi
	if [ -n "$missing" ]; then
		echo "WARNING: The window needs these, which were not found:$missing";
		echo "WARNING: Install them with your package manager; the commands and the service work without them.";
	fi
fi

# ------------------------------------------------------------------
# The package
# ------------------------------------------------------------------
# wget, for the package, MsQuic and the updater. The command that brought this script here also takes curl, as the
# base systems of Fedora, the Red Hat family, openSUSE and Arch have no wget, and Ubuntu's desktop no curl.
function install_wget() {
	if command -v apt-get >/dev/null 2>&1; then
		DEBIAN_FRONTEND=noninteractive apt-get update -qq;
		DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends \
			-o DPkg::Lock::Timeout=300 wget;
	elif command -v dnf >/dev/null 2>&1; then
		dnf install -y -q wget;
	elif command -v zypper >/dev/null 2>&1; then
		ZYPP_LOCK_TIMEOUT=300 zypper --non-interactive --quiet install wget;
	elif command -v pacman >/dev/null 2>&1; then
		pacman -S --noconfirm --needed wget;
	fi
}
if ! command -v wget >/dev/null 2>&1; then
	echo "Installing wget, which $productName's installer and updater need...";
	install_wget;
	if ! command -v wget >/dev/null 2>&1; then
		echo "WARNING: Could not install wget. Install it with your package manager.";
	fi
fi

# Into the install's own folder, not the current one: the updater's is /, where every update used to
# leave the package. A crashed run's leftovers go first. 755 even where an earlier run under a
# stricter umask made them.
mkdir -p "$destinationPath/bin" || exit 1;
chmod 755 "$destinationPath" "$destinationPath/bin";
rm -rf "$destinationPath"/bin/.package.* "$destinationPath"/bin/.staging.*;

# A version already in bin is complete and is kept, a running window's files with it, and nothing is
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
# without it, the service and the commands as well as the window. Most systems have it; a minimal one,
# Debian 13's cloud image among them, does not. Found by glob, as MsQuic is. Debian and Ubuntu carry
# ICU's version in the package's name (libicu76). Arch needs no branch: its pacman cannot run without
# ICU, so it is there wherever pacman is.
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

# nftables, for the kill switch's block and to keep a container's DNS off the local network: the service runs
# nft as the adapter runs ip. Most systems have it; a minimal one, Debian 13's cloud image among them, does not.
# In /usr/sbin, off a person's PATH. Optional: without it the client connects, a warning says a container's DNS
# may reach the local network, and a connect with the kill switch on fails and says why.
function nft_found() {
	[ -x /usr/sbin/nft ] || [ -x /sbin/nft ] || [ -x /usr/bin/nft ];
}
function install_nft() {
	if command -v apt-get >/dev/null 2>&1; then
		DEBIAN_FRONTEND=noninteractive apt-get update -qq;
		DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends \
			-o DPkg::Lock::Timeout=300 nftables;
	elif command -v dnf >/dev/null 2>&1; then
		dnf install -y -q nftables;
	elif command -v zypper >/dev/null 2>&1; then
		ZYPP_LOCK_TIMEOUT=300 zypper --non-interactive --quiet install nftables;
	elif command -v pacman >/dev/null 2>&1; then
		pacman -S --noconfirm --needed nftables;
	fi
}
if ! nft_found; then
	echo "Installing nftables, which $productName's kill switch needs...";
	install_nft;
	if ! nft_found; then
		echo "WARNING: Could not install nftables, without which the kill switch cannot block.";
		echo "WARNING: Install it with your package manager.";
	fi
fi

# MsQuic, for the QUIC channel, once the package is in: a run that fails before changes nothing else.
# Optional everywhere: without it the client uses TCP. Where the distribution has no package for it,
# it goes in the version folder. One try of 30 s at its download: an update installed from its package
# alone, after a disconnect, may find the server out of reach, and a connect made meanwhile is dropped
# at the service's stop below. Such a version, where the distribution has none - Arch's - stays without
# QUIC until an install that reaches the server: left so, as it is rare and TCP still works.
msquic_url="$releaseUrl/$assemblyName-linux-msquic.sh"
if ! msquic_script=$(wget -q --timeout=30 --tries=1 -O- "$msquic_url"); then
	echo "WARNING: Could not download MsQuic installer from: $msquic_url"
	echo "WARNING: wget failed. Skipping MsQuic installation."
elif ! bash <(printf '%s' "$msquic_script") "$binDir"; then
	echo "WARNING: MsQuic installation failed. The client will use TCP."
fi

# A way to set the VPN's DNS: the adapter fails a connect without one rather than let DNS go around the
# tunnel, and a minimal system, Ubuntu 24.04's among them, has none. So where there is neither resolvectl
# nor openresolv, systemd-resolved comes in: Debian's resolvconf refuses the adapter's exclusive entry,
# and systemd's own is resolvectl under another name. After MsQuic, as the machine's DNS changes here.
# One that comes installed and off, as on Arch and AlmaLinux 8, goes on at a new install, or the app could not
# connect there, and when a person runs this script again, which is what the connect's error tells them to do. Not
# at an update, run with -q, when someone may have turned it off since; nor where it is masked, which only a person
# does: for both, the error still names what the app needs.
# One that is on while programs ask another server gets the link, here and at every update: the adapter refuses
# to connect without it, as a server of the local network stays off the tunnel.
# One that runs and does not answer gets a restart, here and at every update, or the connect would time out on it:
# seen in a container, where it was turned on right after its package came in.
function dns_way_found() {
	# resolvectl alone is not enough: Fedora's systemd has it, and the service it talks to is a package of its own
	{ command -v resolvectl >/dev/null 2>&1 && systemctl cat systemd-resolved.service >/dev/null 2>&1; } ||
		resolvconf --version 2>/dev/null | grep -q openresolv;
}
function install_resolved() {
	if command -v apt-get >/dev/null 2>&1; then
		DEBIAN_FRONTEND=noninteractive apt-get update -qq;
		DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends \
			-o DPkg::Lock::Timeout=300 systemd-resolved;
	elif command -v dnf >/dev/null 2>&1; then
		dnf install -y -q systemd-resolved;
	elif command -v zypper >/dev/null 2>&1; then
		ZYPP_LOCK_TIMEOUT=300 zypper --non-interactive --quiet install systemd-resolved;
	fi
}
function machine_dns() {
	awk '$1 == "nameserver" && $2 !~ /^127\./ && $2 != "::1" { print $2 }' /etc/resolv.conf 2>/dev/null | tr '\n' ' ';
}
function on_resolved() {
	[ "$(readlink -f /etc/resolv.conf)" = /run/systemd/resolve/stub-resolv.conf ];
}
function resolved_left_off() {
	{ [ ! -f "$destinationPath/publish.json" ] || [ "$quiet" != "y" ]; } &&
		! resolvconf --version 2>/dev/null | grep -q openresolv &&
		! systemctl is-active --quiet systemd-resolved &&
		[[ "$(systemctl is-enabled systemd-resolved 2>/dev/null)" != masked* ]];
}
function resolved_stuck() {
	systemctl is-active --quiet systemd-resolved && ! timeout 10 resolvectl status >/dev/null 2>&1;
}
# The machine's own DNS servers, the argument, stay as a fallback. With no network manager to tell
# systemd-resolved about them, a plain resolv.conf's are all it has, and without them no name resolves: seen on
# Ubuntu 24.04 and Debian 13. A network manager's servers replace them, and they never take a query from the
# VPN's. They stay at an uninstall: they are the machine's DNS.
function use_resolved() {
	if [ -n "$1" ]; then
		mkdir -p /etc/systemd/resolved.conf.d;
		printf '[Resolve]\nFallbackDNS=%s\n' "$1" >/etc/systemd/resolved.conf.d/vpnhood-machine-dns.conf;
	fi
	# Debian's and Ubuntu's package links it; without the link, programs would not ask systemd-resolved
	on_resolved || ln -sf /run/systemd/resolve/stub-resolv.conf /etc/resolv.conf;
	systemctl enable systemd-resolved;
	systemctl restart systemd-resolved;
	systemctl try-restart NetworkManager 2>/dev/null;
}
if ! dns_way_found; then
	echo "Installing systemd-resolved, which $productName needs to set the VPN's DNS...";
	# Read before the package replaces the file
	dnsServers=$(machine_dns);
	install_resolved;
	if dns_way_found; then
		use_resolved "$dnsServers";
	else
		echo "WARNING: Could not install systemd-resolved, without which $productName cannot connect.";
		echo "WARNING: Install it with your package manager.";
	fi
elif resolved_left_off; then
	# With only a resolver on this machine, the link would leave systemd-resolved with no server to ask
	dnsServers=$(machine_dns);
	if [ -n "$dnsServers" ]; then
		echo "Turning on systemd-resolved, which $productName needs to set the VPN's DNS...";
		use_resolved "$dnsServers";
	fi
elif systemctl is-active --quiet systemd-resolved && ! on_resolved; then
	dnsServers=$(machine_dns);
	if [ -n "$dnsServers" ]; then
		echo "Linking /etc/resolv.conf to systemd-resolved, so that DNS cannot go around the VPN...";
		use_resolved "$dnsServers";
	fi
elif resolved_stuck; then
	echo "Restarting systemd-resolved, which does not answer...";
	systemctl restart systemd-resolved;
fi

# The service goes down for the switch and comes back at the end - which is also what makes this
# script an in-place upgrade. Not before: a package that does not extract leaves it running. A run
# that fails after the stop starts it again on its way out: the version the root names, the new one once
# switched. Add no way back to the one before: a broken release stays until the next, as on Android and
# iOS, which the updater, outside the service, still installs; a rollback is the hard, bug-prone part.
if systemctl is-active --quiet "$assemblyName.service" 2>/dev/null; then
	echo "Stopping the running service...";
	systemctl stop "$assemblyName.service";
	trap 'systemctl start "$assemblyName.service"' EXIT;
fi

# The shared files are replaced by rename, never overwritten in place. The vhupdate running this
# script is still reading its own file, as the launcher behind an open window is - bash reads a
# script as it goes - and an in-place copy would hand them the new file's bytes at their old
# offset. A rename leaves each running one its own file.
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
replace_file "$infoDir/vhupdate" "$destinationPath/vhupdate" 755;
replace_file "$infoDir/vhuninstall" "$destinationPath/vhuninstall" 755;
replace_file "$infoDir/kill-switch-off" "$destinationPath/kill-switch-off" 755;
replace_file "$infoDir/$launcher" "$destinationPath/$launcher" 755;
replace_file "$infoDir/publish.json" "$destinationPath/publish.json" 644;
# a package made on Windows carries no exec bit: the binary, and the VPN host it starts at each connect
chmod +x "$binDir/$assemblyName" "$binDir/$vpnHostName";

# Old versions go only here, at an update: one stays while it is the version this run installs, among
# the three newest, was installed in the last 30 days or a process still runs from it, such as a
# window left open across updates. Version folders at the top, from the layout before bin, count too;
# a folder whose name is not a whole version is left alone.
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

# The storage the service owns, root's alone. Made here rather than on first run so an advanced
# user has a folder to drop a settings.json into before anything has started. Earlier releases
# left it readable by everyone, and a file opened then stays readable through its handle after the
# mode changes - the profiles and the portal session are rewritten in the same file - so a storage
# that is not 700 is copied into a fresh folder and the old one removed: an old handle sees only the
# old files. The service is stopped by now, so nothing writes meanwhile. Not "cp -a": copying the
# folder's "." would give the fresh folder the old one's mode.
storagePath="$destinationPath/storage";
if [ -d "$storagePath" ] && [ "$(stat -c %a "$storagePath")" != "700" ]; then
	echo "Moving the storage into a folder of root's alone...";
	rm -rf "$storagePath.new";
	if ! mkdir -m 700 "$storagePath.new" || ! cp -r --preserve=timestamps "$storagePath/." "$storagePath.new/" ||
		! rm -rf "$storagePath" || ! mv "$storagePath.new" "$storagePath"; then
		echo "Could not move the storage into a fresh folder: $storagePath";
		exit 1;
	fi
fi
mkdir -p -m 700 "$storagePath";

# On the PATH, so "vhclient status" is a command and not a path to remember.
ln -sf "$destinationPath/$launcher" "/usr/local/bin/$launcher";

# ------------------------------------------------------------------
# The service
# ------------------------------------------------------------------
# "daemon", not the bare binary: the bare binary now opens a window, and a unit that asks for one
# on a machine with no display restarts forever without ever saying why. No ExecStop: systemd's
# SIGTERM stops the daemon, which disconnects the tunnel before it exits. The paths are quoted: the
# install's folder may hold a space. network-online rather than network, which says nothing of a
# working network: always on connects as the daemon starts, and would otherwise wait out a pause first.
echo "Writing the $assemblyName service...";
service="
[Unit]
Description=$productName
Wants=network-online.target
After=network-online.target

[Service]
Type=simple
ExecStart=\"$destinationPath/$launcher\" daemon
TimeoutStartSec=0
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
";
echo "$service" > "/etc/systemd/system/$assemblyName.service";

# The updater: vhupdate once per run, by its timer at boot and every 12 hours. -waitForIdle: it installs only while
# the VPN is idle, and the service hands a download back after the next disconnect. The unit of earlier releases
# restarted itself every 12 hours and was enabled for the boot: disabled before it is replaced, and stopped unless
# this script runs inside it, so neither its boot entry nor a restart it is waiting for is left.
# network-online: at boot the first check would otherwise run before there is a route, fail, and wait 12 hours.
echo "Writing the $assemblyName updater...";
updaterUnitFile="/etc/systemd/system/${assemblyName}Updater.service";
if [ -f "$updaterUnitFile" ] && ! grep -qx "Type=oneshot" "$updaterUnitFile"; then
	systemctl disable "${assemblyName}Updater.service" >/dev/null 2>&1;
	if [ "$insideUpdaterUnit" != "y" ]; then
		systemctl stop "${assemblyName}Updater.service";
	fi
fi
updaterService="
[Unit]
Description=$productName Updater
Wants=network-online.target
After=network-online.target

[Service]
Type=oneshot
ExecStart=\"$destinationPath/vhupdate\" -waitForIdle
";
echo "$updaterService" > "$updaterUnitFile";
updaterTimer="
[Unit]
Description=$productName Updater

[Timer]
OnBootSec=5min
OnUnitActiveSec=12h
RandomizedDelaySec=10min

[Install]
WantedBy=timers.target
";
echo "$updaterTimer" > "/etc/systemd/system/${assemblyName}Updater.timer";

systemctl daemon-reload;

if [ "$autostart" = "y" ]; then
	systemctl enable "$assemblyName.service";
else
	systemctl disable "$assemblyName.service" >/dev/null 2>&1;
fi

# Its own choice, apart from the boot's, which is the timer's. The updater's run is never stopped from here: inside
# it, this script would stop with it; outside, the timer's next run takes the unit file and the vhupdate written
# here anyway.
if [ "$autoupdate" = "y" ]; then
	systemctl enable --now "${assemblyName}Updater.timer";
else
	systemctl disable --now "${assemblyName}Updater.timer" >/dev/null 2>&1;
fi

# Started either way: a person who said no to "at boot" still expects the app they just installed
# to work now, and the window would otherwise have to start it on its first launch.
systemctl restart "$assemblyName.service";

# And waited for. systemctl returns once systemd has STARTED the unit, but the service opens the
# channel its commands ask for the address on a few seconds later, when its listener has bound.
# Without this the closing message below invites somebody to run a command that is about to fail
# on a service which is coming up perfectly.
channelSocket="/run/$assemblyName/daemon.sock";
echo -n "Waiting for the service";
for _ in $(seq 1 40); do
	if [ -S "$channelSocket" ]; then
		break;
	fi
	echo -n ".";
	sleep 1;
done
echo "";
if [ ! -S "$channelSocket" ]; then
	echo "WARNING: The service was started but has not answered yet.";
	echo "WARNING: Check it with: journalctl -u $assemblyName -n 50";
fi

# ------------------------------------------------------------------
# The window
# ------------------------------------------------------------------
if [ "$withDesktop" == "y" ]; then
	echo "Adding $productName to the application menu...";

	# The head's own icon, published beside the binary: AppIcon.png, the 256 px picture of the icon
	# the head names (ApplicationIcon), placed at that size. Older installers put the UI's 60 px logo
	# in 512x512, which the dock blew up into a blur, so it goes. No icon, and the entry is still a
	# working entry, with the desktop's generic one.
	iconLine="";
	iconDir="/usr/share/icons/hicolor/256x256/apps";
	rm -f "/usr/share/icons/hicolor/512x512/apps/$assemblyName.png";
	if [ -f "$binDir/AppIcon.png" ] && mkdir -p "$iconDir" && cp -f "$binDir/AppIcon.png" "$iconDir/$assemblyName.png" &&
		chmod 644 "$iconDir/$assemblyName.png"; then
		iconLine="Icon=$assemblyName";
	fi
	if [ -z "$iconLine" ]; then
		echo "WARNING: Could not place the application icon; the menu entry will use a generic one.";
	fi

	# Uninstall on the entry's own right-click menu, where the desktops put it for the apps their
	# software centre installed; Linux has no list of installed apps for a script's install to join,
	# and an uninstall icon of its own in the app grid is clutter. Beside it the kill switch's way out,
	# for a person whose app cannot open while the internet is blocked, under the app's name, which heads
	# the menu (the Kill Switch page names it: FeaturePages.KillSwitchOffEntryName), which says what it did in a
	# notification. pkexec asks for an administrator's password; without it both stay commands. GNOME's menu
	# shows an action an update adds only from the next sign-in (seen on Ubuntu 26.04).
	actionLines="";
	if command -v pkexec >/dev/null 2>&1; then
		actionLines="Actions=KillSwitchOff;Uninstall;

[Desktop Action KillSwitchOff]
Name=Turn off kill switch
Exec=pkexec \"$destinationPath/kill-switch-off\" -notify \"$productName\"

[Desktop Action Uninstall]
Name=Uninstall
Exec=pkexec \"$destinationPath/vhuninstall\"";
	fi

	desktopEntry="[Desktop Entry]
Type=Application
Name=$productName
Comment=Undetectable VPN
Exec=/usr/local/bin/$launcher ui
$iconLine
Terminal=false
Categories=Network;Security;
Keywords=VPN;Privacy;Proxy;
StartupNotify=true
$actionLines
";
	# -desktop on a machine with none yet, where a bare Debian or Ubuntu has no such folder
	mkdir -p /usr/share/applications;
	echo "$desktopEntry" > "/usr/share/applications/$assemblyName.desktop";
	# readable by the desktop even where an earlier run left the file at its caller's umask, as the icon
	chmod 644 "/usr/share/applications/$assemblyName.desktop";
	update-desktop-database /usr/share/applications >/dev/null 2>&1;

	# The tray at every sign-in, one entry for everyone: an administrator's shows the app's icon where
	# the desktop shows tray icons, the window closed; anyone else's, or one on a desktop that shows
	# none, ends at once and shows nothing.
	autostartEntry="[Desktop Entry]
Type=Application
Name=$productName
Exec=/usr/local/bin/$launcher ui --tray
$iconLine
Terminal=false
";
	mkdir -p /etc/xdg/autostart;
	echo "$autostartEntry" > "/etc/xdg/autostart/$assemblyName.desktop";
	chmod 644 "/etc/xdg/autostart/$assemblyName.desktop";
	if command -v gtk-update-icon-cache >/dev/null 2>&1; then
		gtk-update-icon-cache -f -t /usr/share/icons/hicolor >/dev/null 2>&1;
	fi
fi

# ------------------------------------------------------------------
echo "";
echo "$productName has been installed.";
if [ "$withDesktop" == "y" ]; then
	echo "  Open the window from the application menu, or run: $launcher ui";
fi
echo "  From a terminal:";
echo "    $launcher connect";
echo "    $launcher status";
echo "    $launcher --help";
echo "  To uninstall it: sudo \"$destinationPath/vhuninstall\"";
} # the group opened at the top
