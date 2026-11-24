#!/bin/bash
# The uninstaller of a server installed by a user without root: install.sh beside it places it in the install's
# folder as vhuninstall, and it runs as that user. It removes what starts and updates the server, what runs from
# the install, and its files; the storage - settings, access keys, certificates - stays for a later install unless
# -purge. Root's install has its own, pub/lib/vh-installer/linux/uninstall.sh.
# One group, which bash reads whole before it runs any of it: this file is deleted on the way.
{
productName="$(productNameParam)";
assemblyName="$(assemblyNameParam)";

# The install's folder is this script's own
destinationPath="$(dirname "$(readlink -f "$0")")";

function show_help() {
	echo "Usage: $destinationPath/vhuninstall [options]";
	echo "  -purge                delete the settings and data in $destinationPath/storage too";
	echo "  -h, -help             this";
	echo "Options take one dash or two: -purge or --purge.";
}

purge="";
for arg in "$@"; do
	# An option takes two dashes too, as the GNU tools' long options do
	if [ "${arg:0:2}" = "--" ]; then
		arg="${arg:1}";
	fi

	if [ "$arg" = "-purge" ]; then
		purge="y";
	elif [ "$arg" = "-help" ] || [ "$arg" = "-h" ]; then
		show_help;
		exit 0;
	else
		echo "Unknown argument! argument: $arg (-help lists the options)";
		exit 1;
	fi
done

if [ "$(id -u)" = "0" ]; then
	echo "Run this as the user who installed $productName, without sudo: their services are in their own manager.";
	exit 1;
fi

# The installer's lock: an install running now, the updater's say, would write back part of what this removes, so
# this waits for it, as an install started meanwhile waits for this.
if command -v flock >/dev/null 2>&1; then
	exec 9>>"$destinationPath/.install.lock";
	if ! flock -n 9; then
		echo "Another install or uninstall of $productName is running. Waiting for it to finish...";
		flock 9;
	fi
fi

# Nothing is deleted but an install's folder, which has its publish.json
if [ ! -f "$destinationPath/publish.json" ]; then
	echo "Not an install of $productName: $destinationPath";
	exit 1;
fi

echo "Uninstalling $productName...";

# What starts it and updates it first, which would start it again: cron's lines, which end with the unit's name,
# or the user's own services
if crontab -l 2>/dev/null | grep -qE " # ${assemblyName}(Updater)?\$"; then
	crontab -l 2>/dev/null | grep -vE " # ${assemblyName}(Updater)?\$" | crontab -;
fi
# systemctl --user finds the user's own manager here, where a shell that came by su has no XDG_RUNTIME_DIR
if [ -z "$XDG_RUNTIME_DIR" ] && [ -d "/run/user/$(id -u)" ]; then
	export XDG_RUNTIME_DIR="/run/user/$(id -u)";
fi
unitDir="$HOME/.config/systemd/user";
units="";
for unit in "$assemblyName.service" "${assemblyName}Updater.service"; do
	if [ -f "$unitDir/$unit" ]; then
		systemctl --user disable --now "$unit" >/dev/null 2>&1;
		rm -f "$unitDir/$unit";
		units="y";
	fi
done
if [ -n "$units" ]; then
	systemctl --user daemon-reload;
	systemctl --user reset-failed "$assemblyName.service" "${assemblyName}Updater.service" >/dev/null 2>&1;
fi

# What still runs from the install - a server cron started, or the person - ends by SIGTERM, as on its stop
# command; its loop then ends with it
function running_pids() {
	local proc;
	for proc in /proc/[0-9]*; do
		if [[ $(readlink "$proc/exe" 2>/dev/null) == "$destinationPath/bin/"* ]]; then
			echo "${proc#/proc/}";
		fi
	done
}
pids=$(running_pids);
if [ -n "$pids" ]; then
	echo "Stopping what still runs from $destinationPath...";
	# shellcheck disable=SC2086
	kill -TERM $pids 2>/dev/null;
	for _ in $(seq 1 20); do
		pids=$(running_pids);
		[ -z "$pids" ] && break;
		sleep 0.5;
	done
	# shellcheck disable=SC2086
	[ -n "$pids" ] && kill -KILL $pids 2>/dev/null;
fi

# The lingering the install turned on, which it marked: the user's manager need not run from boot on any more
if [ -f "$destinationPath/.linger" ]; then
	loginctl --no-ask-password disable-linger >/dev/null 2>&1 ||
		echo "WARNING: Could not turn off the lingering the install turned on: run loginctl disable-linger";
fi

if [ "$purge" = "y" ]; then
	rm -rf "$destinationPath";
else
	find "$destinationPath" -mindepth 1 -maxdepth 1 ! -name storage -exec rm -rf {} +;
	rmdir "$destinationPath" 2>/dev/null;
fi

echo "$productName has been uninstalled.";
if [ -d "$destinationPath/storage" ]; then
	echo "  Its settings and data are kept for a later install: $destinationPath/storage";
	echo "  To remove them too: rm -rf \"$destinationPath\"";
fi
exit 0;
} # the group opened at the top
