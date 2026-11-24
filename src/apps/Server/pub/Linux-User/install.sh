#!/bin/bash
# VpnHood! SERVER for a user without root: in their home folder, started at boot and updated by their own
# systemd services where the system lets those run from boot on (lingering), else by cron. Root's install
# is pub/lib/vh-installer/linux/install.sh, which shares nothing with this one but the launcher: the package
# carries this install's updater and uninstaller too (updater.sh and uninstall.sh, beside this file), which
# run as the user. One script for both CPUs: it picks the package itself.
# One group, which bash reads whole before it runs any of it: the updater pipes nothing, but a download
# cut short would otherwise run as far as it got.
{
echo "$(productNameParam) Installation for this user, without root";

# What this makes is everyone's to read and the user's to write, whatever umask the caller brings
umask 022;

releaseUrl="$(releaseUrlParam)";
versionTag="$(versionTagParam)";
assemblyName="$(assemblyNameParam)";
productName="$(productNameParam)";
launcher="$(launcherNameParam)";

destinationPath="$HOME/$assemblyName";
# Linux lets only root use the ports below 1024, 443 among them
defaultPort=8443;
# The client protocols the server speaks, ServerHost's MinProtocolVersion and MaxProtocolVersion
minProtocolVersion=8;
maxProtocolVersion=15;

function show_help() {
	echo "Usage: $assemblyName-linux-user.sh [options]";
	echo "Installs $productName for this user alone, in ~/$assemblyName, without root. Apps then always";
	echo "use Cloak Mode, the server listens on a port above 1023, and it changes no operating system settings.";
	echo "  -q                    no questions: what is not given keeps the install's choices,";
	echo "                        and a new install starts at boot and updates itself";
	echo "  -noautostart          do not start the server at boot";
	echo "  -noautoupdate         do not update the server by itself";
	echo "  -port <n>             a new install's TCP port, 1024-65535; $defaultPort unless set";
	echo "  -minClientProtocolVersion <n>";
	echo "                        a new install's oldest app protocol accepted, $minProtocolVersion-$maxProtocolVersion: $minProtocolVersion, the oldest";
	echo "                        supported, keeps the gate open to every app; $maxProtocolVersion resists probes, and";
	echo "                        older apps cannot connect";
	echo "  -h, -help             this";
	echo "Options take one dash or two: -noautoupdate or --noautoupdate.";
}

# Read arguments. The updater runs this script too, with -q and -destination.
autostart="";
autoupdate="";
port="";
minClientProtocolVersion="";
for i;
do
arg=$i;
# An option takes two dashes too, as the GNU tools' long options do; a value is left as it is.
if [ "$lastArg" = "" ] && [ "${arg:0:2}" = "--" ]; then
	arg="${arg:1}";
fi

if [ "$arg" = "-noautostart" ]; then
	autostart="n";
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

elif [ "$lastArg" = "-port" ]; then
	port=$arg;
	lastArg=""; continue;

elif [ "$lastArg" = "-minClientProtocolVersion" ]; then
	minClientProtocolVersion=$arg;
	lastArg=""; continue;

# The install's folder; the updater passes its own.
elif [ "$lastArg" = "-destination" ]; then
	destinationPath=$arg;
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

# Not root's: root installs the server for the whole machine, with what this one goes without
if [ "$(id -u)" = "0" ]; then
	echo "This installer is for a user without root: run it as that user, without sudo.";
	echo "As root, install $productName for the whole machine:";
	echo "  bash -c 'u=$releaseUrl/$assemblyName-linux.sh; bash <(wget -qO- \$u 2>/dev/null || curl -fsSL \$u)'";
	exit 1;
fi

# The build needs glibc. On musl, Alpine's, the server cannot start, so nothing goes in. By what ldd says: a
# machine that cannot say is not refused.
if ldd --version 2>&1 | grep -qi musl; then
	echo "$productName cannot run on this system: it needs glibc, and this system uses musl, as Alpine does.";
	exit 1;
fi

# The package of this machine's CPU. A 32-bit userland on a 64-bit kernel, as Raspberry Pi OS 32-bit has, says
# aarch64 too, yet runs no arm64 build.
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

# The x64 build's .NET needs SSE3, SSSE3, SSE4.1, SSE4.2 and POPCNT (x86-64-v2), and without them stops as the server
# starts, before any of its code runs. By the flags the kernel lists: a machine that cannot list them is not refused.
cpuFlags=$(grep -m1 '^flags' /proc/cpuinfo 2>/dev/null);
if [ "$machineCpu" = "x64" ] && [ -n "$cpuFlags" ]; then
	for flag in pni ssse3 sse4_1 sse4_2 popcnt; do
		if ! grep -qw "$flag" <<< "$cpuFlags"; then
			echo "$productName cannot run on this machine: it needs a processor with SSE4.2 and POPCNT, and this one does not have them.";
			exit 1;
		fi
	done
fi

# What a user cannot add, checked before anything changes: only root can install a package.
if [ -z "$HOME" ] || [ ! -d "$HOME" ]; then
	echo "This user has no home folder (\$HOME) to install $productName in.";
	exit 1;
fi
# the folder goes into cron's lines and systemd's units, which read none of these as a name
if [[ "$destinationPath" == *[\"\\\$\`%]* ]] || [[ "$destinationPath" == *$'\n'* ]] || [[ "$destinationPath" != /* ]]; then
	echo "Install $productName in a folder whose full path has none of \" \\ \$ \` % or a new line: $destinationPath";
	exit 1;
fi
# ICU: .NET reads its culture data from it and stops at start without it. Most systems have it; a minimal one,
# Debian 13's cloud image among them, does not. Found by glob: a minimal image has no find.
function icu_found() {
	local icuLib;
	for icuLib in /usr/lib64/libicuuc.so.* /usr/lib/libicuuc.so.* /usr/lib/*/libicuuc.so.*; do
		[ -e "$icuLib" ] && return 0;
	done
	return 1;
}
if ! icu_found; then
	echo "$productName needs ICU (libicu), which this system does not have, and which only root can install.";
	echo "Ask the administrator to install it: on Debian and Ubuntu, the libicu package of the release,";
	echo "such as libicu76; on Fedora and the Red Hat family, libicu.";
	exit 1;
fi
if ! command -v wget >/dev/null 2>&1 && ! command -v curl >/dev/null 2>&1; then
	echo "$productName's installer and updater need wget or curl, which this system does not have, and";
	echo "which only root can install. Ask the administrator to install one of them.";
	exit 1;
fi

function is_port() {
	[[ "$1" =~ ^[0-9]{4,5}$ ]] && [ "$((10#$1))" -ge 1024 ] && [ "$((10#$1))" -le 65535 ];
}
function is_protocol_version() {
	[[ "$1" =~ ^[0-9]{1,3}$ ]] && [ "$((10#$1))" -ge "$minProtocolVersion" ] && [ "$((10#$1))" -le "$maxProtocolVersion" ];
}
if [ -n "$port" ] && ! is_port "$port"; then
	echo "-port takes a number from 1024 to 65535: Linux lets only root use the ports below.";
	exit 1;
fi
if [ -n "$minClientProtocolVersion" ] && ! is_protocol_version "$minClientProtocolVersion"; then
	echo "-minClientProtocolVersion takes a number from $minProtocolVersion to $maxProtocolVersion.";
	exit 1;
fi

if [ "$versionTag" == "" ]; then
	echo "Could not find versionTag!";
	exit 1;
fi
binDir="$destinationPath/bin/$versionTag";
packageUrl="$releaseUrl/$assemblyName-linux-$machineCpu.tar.gz";
settingsFile="$destinationPath/storage/appsettings.json";
userUnitDir="$HOME/.config/systemd/user";

# systemctl --user finds the user's own manager here, where a shell that came by su has no XDG_RUNTIME_DIR
if [ -z "$XDG_RUNTIME_DIR" ] && [ -d "/run/user/$(id -u)" ]; then
	export XDG_RUNTIME_DIR="/run/user/$(id -u)";
fi

# Run by the updater's unit, this script is inside its cgroup: vhupdate runs it inline, and restarting that
# unit from here would stop this script with it.
insideUpdaterUnit="n";
if grep -qF "/${assemblyName}Updater.service" /proc/self/cgroup 2>/dev/null; then
	insideUpdaterUnit="y";
fi

# What starts the server at boot and updates it: the user's own systemd services, or cron, whose lines end
# with the unit's name they stand for. One an earlier run chose stays.
function cron_lines() {
	crontab -l 2>/dev/null;
}
function current_runner() {
	if [ -f "$userUnitDir/$assemblyName.service" ] || [ -f "$userUnitDir/${assemblyName}Updater.service" ]; then
		echo "systemd";
	elif cron_lines | grep -qE " # ${assemblyName}(Updater)?\$"; then
		echo "cron";
	fi
}

# A choice no option made keeps the install's own, so an update - run with -q alone - changes none; a new
# install starts at boot and updates itself. Asked unless -q, with that as the answer Enter gives. systemd's
# choice is the unit's link in default.target.wants, which needs no manager to read; cron's, its line; with
# neither, a file in the install's folder named after the choice. An install is this installer's once its
# vhuninstall is there: a folder without one, a server run by hand from it among them, is a new install.
# unit_choice <unit> <choice>
function unit_choice() {
	if [ ! -f "$destinationPath/vhuninstall" ]; then
		echo "y";
		return;
	fi
	case "$(current_runner)" in
		systemd) [ -e "$userUnitDir/default.target.wants/$1" ] && echo "y" || echo "n";;
		cron) cron_lines | grep -qE " # ${1%.service}\$" && echo "y" || echo "n";;
		*) [ -e "$destinationPath/.$2" ] && echo "y" || echo "n";;
	esac
}
function ask() {
	local answer hint="Y/n";
	if [ "$2" = "n" ]; then hint="y/N"; fi
	read -r -p "$1 ($hint)? " answer;
	answer="${answer,,}";
	if [ "${answer:-$2}" = "y" ] || [ "${answer:0:1}" = "y" ]; then echo "y"; else echo "n"; fi
}
defaultAutostart=$(unit_choice "$assemblyName.service" autostart);
defaultAutoupdate=$(unit_choice "${assemblyName}Updater.service" autoupdate);
if [ "$quiet" != "y" ]; then
	[ -z "$autostart" ] && autostart=$(ask "Start the server at boot" "$defaultAutostart");
	[ -z "$autoupdate" ] && autoupdate=$(ask "Keep the server up to date by itself" "$defaultAutoupdate");
fi
autostart=${autostart:-$defaultAutostart};
autoupdate=${autoupdate:-$defaultAutoupdate};

# A new install's settings: its port, as root's 443 is closed to a user, and the oldest app protocol it accepts.
# Asked unless -q. Settings that are there already are the person's, these two in them too.
function ask_protocol_version() {
	local answer;
	while true; do
		read -r -p "Minimum client protocol version ($minProtocolVersion-$maxProtocolVersion, Enter for $minProtocolVersion)? " answer;
		answer="${answer:-$minProtocolVersion}";
		if is_protocol_version "$answer"; then echo "$answer"; return; fi
		echo "Enter a number from $minProtocolVersion to $maxProtocolVersion." >&2;
	done
}
function ask_port() {
	local answer;
	while true; do
		read -r -p "TCP port the server listens on (1024-65535, Enter for $defaultPort)? " answer;
		answer="${answer:-$defaultPort}";
		if is_port "$answer"; then echo "$answer"; return; fi
		echo "Enter a number from 1024 to 65535: Linux lets only root use the ports below." >&2;
	done
}
newSettings="";
if ! grep -qs '"' "$settingsFile"; then
	newSettings="y";
	if [ "$quiet" != "y" ] && [ -z "$minClientProtocolVersion" ]; then
		echo "The oldest app protocol this server accepts. $minProtocolVersion, the oldest supported, keeps the gate open to";
		echo "every app; $maxProtocolVersion resists probes, and apps older than it cannot connect.";
		minClientProtocolVersion=$(ask_protocol_version);
	fi
	if [ "$quiet" != "y" ] && [ -z "$port" ]; then
		port=$(ask_port);
	fi
	# as JSON numbers: 08 is none
	port=$((10#${port:-$defaultPort}));
	minClientProtocolVersion=$((10#${minClientProtocolVersion:-$minProtocolVersion}));
elif [ -n "$port" ] || [ -n "$minClientProtocolVersion" ]; then
	echo "The server's settings are there already: set its port in TcpEndPoints, and its minimum client";
	echo "protocol in MinClientProtocolVersion, in $settingsFile.";
	exit 1;
fi

# One install at a time from here, where this run starts to change things: the updater's, the person's, the
# uninstall. Beside another, the one that ends last has the last word, and each deletes the other's download
# as a crashed run's leftovers. The lock goes when its run ends, even by a crash.
mkdir -p "$destinationPath/bin" || exit 1;
if command -v flock >/dev/null 2>&1; then
	exec 9>>"$destinationPath/.install.lock";
	if ! flock -n 9; then
		echo "Another install or uninstall of $productName is running. Waiting for it to finish...";
		flock 9;
	fi
fi

# Download & install the module. A crashed run's leftovers go first. A version already in bin is complete and
# is kept, a running server's files with it, and nothing is downloaded for it. Another is extracted into a
# staging folder that is renamed into bin whole, so a version in bin is never half there.
chmod 755 "$destinationPath" "$destinationPath/bin";
rm -rf "$destinationPath"/bin/.package.* "$destinationPath"/bin/.staging.*;
if [ -d "$binDir" ]; then
	echo "Already installed: $binDir";
else
	echo "Downloading $productName...";
	packageFile=$(mktemp "$destinationPath/bin/.package.XXXXXX") || exit 1;
	if command -v wget >/dev/null 2>&1; then
		wget -nv -O "$packageFile" "$packageUrl";
	else
		curl -fsSL --retry 2 -o "$packageFile" "$packageUrl";
	fi
	if [ $? -ne 0 ]; then
		echo "Could not download $packageUrl";
		rm -f "$packageFile";
		exit 1;
	fi

	# Plain modes: a package made on Windows records 0666 and 0777. The folder must be this version's, or the
	# rename would put something else in its place. touch: tar keeps the build's times, and the cleanup below
	# goes by when a version was installed.
	echo "Extracting to $binDir";
	stagingDir=$(mktemp -d "$destinationPath/bin/.staging.XXXXXX") || exit 1;
	if ! tar --no-same-permissions -xzf "$packageFile" -C "$stagingDir" ||
		[ ! -d "$stagingDir/$versionTag" ] || ! chmod -R u=rwX,go=rX "$stagingDir/$versionTag" ||
		! touch "$stagingDir/$versionTag" || ! mv "$stagingDir/$versionTag" "$binDir"; then
		echo "Could not extract $versionTag from $packageFile";
		rm -rf "$stagingDir" "$packageFile";
		exit 1;
	fi
	rm -rf "$stagingDir" "$packageFile";
fi

# The shared files are replaced by rename, never rewritten in place: the vhupdate running this script is still
# reading its own file, as bash reads a script as it goes. publish.json, which switches the version, goes last.
# The package carries a user install's updater and uninstaller (this folder's updater.sh and uninstall.sh, as
# vhupdate-user and vhuninstall-user) beside root's, which it does not take.
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
replace_file "$infoDir/vhupdate-user" "$destinationPath/vhupdate" 755;
replace_file "$infoDir/vhuninstall-user" "$destinationPath/vhuninstall" 755;
replace_file "$infoDir/$launcher" "$destinationPath/$launcher" 755;
replace_file "$infoDir/publish.json" "$destinationPath/publish.json" 644;

# Old versions go only here, at an update: one stays while it is the version this run installs, among the three
# newest, was installed in the last 30 days or a process still runs from it.
function remove_old_versions() {
	local versionDirs=() versionDir newest running versionPattern='^v[0-9]+(\.[0-9]+){1,3}(-.+)?$';
	local installing="$(readlink -f "$binDir")";
	for versionDir in "$destinationPath"/bin/v[0-9]*; do
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

# The storage, the user's alone: the server keeps its access keys and certificates there
storagePath="$destinationPath/storage";
mkdir -p "$storagePath" && chmod 700 "$storagePath";

# A new install's settings, settled above, through a new file
if [ -n "$newSettings" ]; then
	settings="{
  \"FileAccessManager\": {
    \"TcpEndPoints\": [ \"0.0.0.0:$port\", \"[::]:$port\" ],
    \"Session\": {
      \"MinClientProtocolVersion\": $minClientProtocolVersion
    }
  }
}";
	if ! echo "$settings" > "$settingsFile.new" || ! chmod 600 "$settingsFile.new" || ! mv -f "$settingsFile.new" "$settingsFile"; then
		echo "Could not write $settingsFile";
		exit 1;
	fi
	echo "TCP port: $port";
	echo "Minimum client protocol version: $minClientProtocolVersion";
fi

# The runner, chosen once, by the first run that is to start the server at boot or update it: the user's own
# systemd services where the user's manager runs from boot on, which lingering does - turned on here where the
# system lets a user do it for themselves, and marked so the uninstall turns it off again - else cron. Neither,
# and the server runs only until the machine restarts, with no updates.
function linger_on() {
	local lingerFile="/var/lib/systemd/linger/$(id -un)";
	[ -e "$lingerFile" ] && return 0;
	loginctl --no-ask-password enable-linger >/dev/null 2>&1 && [ -e "$lingerFile" ] || return 1;
	touch "$destinationPath/.linger";
}
# A crontab with no cron daemon to read it runs nothing: the daemon is seen among the processes, or as a unit,
# where the system hides other users' processes
function cron_runs() {
	command -v crontab >/dev/null 2>&1 || return 1;
	grep -qxE 'cron|crond|fcron' /proc/[0-9]*/comm 2>/dev/null ||
		systemctl is-active --quiet cron.service crond.service cronie.service 2>/dev/null;
}
rm -f "$destinationPath/.autostart" "$destinationPath/.autoupdate";
runner=$(current_runner);
if [ -z "$runner" ] && { [ "$autostart" = "y" ] || [ "$autoupdate" = "y" ]; }; then
	if systemctl --user show-environment >/dev/null 2>&1 && linger_on; then
		runner="systemd";
	elif cron_runs; then
		runner="cron";
	else
		runner="none";
	fi
fi

if [ "$runner" = "systemd" ]; then
	# The units. The paths are quoted: the install's folder may hold a space. Starting at boot and updating are
	# two choices: a unit neither ever asked for is not written, and one that is there stays, disabled, when its
	# choice is no. A user's manager has no network-online.target to wait for.
	mkdir -p "$userUnitDir" || exit 1;
	serviceUnit="$userUnitDir/$assemblyName.service";
	updaterUnit="$userUnitDir/${assemblyName}Updater.service";

	if [ "$autostart" = "y" ] || [ -f "$serviceUnit" ]; then
		echo "Writing the $assemblyName service...";
		echo "[Unit]
Description=$productName

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
WantedBy=default.target" > "$serviceUnit";
	fi

	if [ "$autoupdate" = "y" ] || [ -f "$updaterUnit" ]; then
		echo "Writing the $assemblyName updater service...";
		echo "[Unit]
Description=$productName Updater

[Service]
Type=simple
ExecStart=\"$destinationPath/vhupdate\"
TimeoutStartSec=0
Restart=always
RestartSec=720min

[Install]
WantedBy=default.target" > "$updaterUnit";
	fi

	systemctl --user daemon-reload;

	# A server that runs now runs the new version, whether or not it starts at boot
	if [ -f "$serviceUnit" ]; then
		if [ "$autostart" = "y" ]; then
			systemctl --user enable "$assemblyName.service";
		else
			systemctl --user disable "$assemblyName.service" 2>/dev/null;
		fi
		if [ "$autostart" = "y" ] || systemctl --user is-active --quiet "$assemblyName.service"; then
			systemctl --user restart "$assemblyName.service";
		fi
	fi

	# Never stopped from inside it: this script would stop with it and nothing would start it again. Its next run
	# takes the unit file and the vhupdate written here anyway.
	if [ -f "$updaterUnit" ]; then
		if [ "$autoupdate" = "y" ]; then
			systemctl --user enable "${assemblyName}Updater.service";
			if [ "$insideUpdaterUnit" != "y" ]; then
				systemctl --user restart "${assemblyName}Updater.service";
			fi
		else
			systemctl --user disable "${assemblyName}Updater.service" 2>/dev/null;
			if [ "$insideUpdaterUnit" != "y" ]; then
				systemctl --user stop "${assemblyName}Updater.service";
			fi
		fi
	fi
else
	# cron's lines, each ending with the unit's name it stands for: none is left for a no. The server runs in a
	# loop that holds a lock, so it runs once, starts it again 10 seconds after a failure, as the unit does, and
	# ends with a clean stop, or once the install is gone. Its errors are the last run's, beside it in
	# vhserver.log, as the updater's last run is in vhupdate.log.
	runLock="$destinationPath/.run.lock";
	serverLoop='while ! "$0" >/dev/null 2>"$1" && [ -x "$0" ]; do sleep 10; done';
	if [ "$runner" = "cron" ]; then
		echo "Writing the $assemblyName cron lines...";
		updateHour=$((RANDOM % 12));
		{
			cron_lines | grep -vE " # ${assemblyName}(Updater)?\$";
			if [ "$autostart" = "y" ]; then
				echo "@reboot flock -n \"$runLock\" sh -c '$serverLoop' \"$destinationPath/$launcher\" \"$destinationPath/vhserver.log\" # $assemblyName";
			fi
			if [ "$autoupdate" = "y" ]; then
				echo "$((RANDOM % 60)) $updateHour,$((updateHour + 12)) * * * \"$destinationPath/vhupdate\" >\"$destinationPath/vhupdate.log\" 2>&1 # ${assemblyName}Updater";
			fi
		} | crontab - || exit 1;
	fi

	# What runs from the install, as the uninstall finds it: the folder as /proc names it, with no link in it
	realDestinationPath=$(readlink -f "$destinationPath");
	function running_pids() {
		local proc;
		for proc in /proc/[0-9]*; do
			if [[ $(readlink "$proc/exe" 2>/dev/null) == "$realDestinationPath/bin/"* ]]; then
				echo "${proc#/proc/}";
			fi
		done
	}

	# A server that runs now runs the new version, whether or not it starts at boot: stopped as systemd stops a
	# unit, and started again. Its loop then ends, and the new one takes the lock; a loop that started it again
	# meanwhile, after a stop that failed, runs the new version already, as the launcher reads it.
	if [ "$autostart" = "y" ] || [ -n "$(running_pids)" ]; then
		pids=$(running_pids);
		if [ -n "$pids" ]; then
			# shellcheck disable=SC2086
			kill -TERM $pids 2>/dev/null;
			for _ in $(seq 1 30); do
				[ -z "$(running_pids)" ] && break;
				sleep 1;
			done
			pids=$(running_pids);
			# shellcheck disable=SC2086
			[ -n "$pids" ] && kill -KILL $pids 2>/dev/null;
		fi
		# In a session of its own, which the terminal's hang-up does not reach. Without this run's locks, the
		# install's and the updater's, which the server would otherwise hold as long as it runs.
		echo "Starting $productName...";
		setsid flock -w 15 "$runLock" sh -c "$serverLoop" "$destinationPath/$launcher" "$destinationPath/vhserver.log" \
			>/dev/null 2>&1 </dev/null 8>&- 9>&- &
	fi

	# With neither, the choices are kept in the install's folder, for this install run again to start the server,
	# and an update to start it again, as a unit would; and for a runner the system may have by the next run.
	if [ "$runner" = "none" ]; then
		[ "$autostart" = "y" ] && touch "$destinationPath/.autostart";
		[ "$autoupdate" = "y" ] && touch "$destinationPath/.autoupdate";
		echo "WARNING: Neither systemd's services for this user nor cron can run $productName here at boot, so";
		[ "$autostart" = "y" ] && echo "WARNING: it does not start by itself after the machine restarts: run this install again then.";
		[ "$autoupdate" = "y" ] && echo "WARNING: it does not update itself: run \"$destinationPath/vhupdate\" now and then.";
	fi
fi

# show final message
echo "$productName has been installed for $(id -un). Run the following command:";
echo "$destinationPath/$launcher";
echo "To uninstall it: \"$destinationPath/vhuninstall\"";

# The firewall is root's to open: this says only that there is one
if systemctl is-active --quiet firewalld 2>/dev/null; then
	echo "NOTE: firewalld is active on this machine. Clients reach the server only through ports it allows,";
	echo "      which only root can open: ask the administrator to allow the server's TCP port.";
fi
} # the group opened at the top
