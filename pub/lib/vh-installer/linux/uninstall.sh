#!/bin/bash
# The uninstaller, which the installer places beside the launcher as vhuninstall: the server's and the
# desktop client's alike, since it removes only what it finds. The storage - settings, profiles,
# certificates - stays for a later install unless -purge, as apt's remove keeps what its purge deletes.
# One group, which bash reads whole before it runs any of it: this file is deleted on the way.
{
productName="$(productNameParam)";
assemblyName="$(assemblyNameParam)";
launcher="$(launcherNameParam)";

# The install's folder is this script's own: the installer may have been given another than /opt's.
destinationPath="$(dirname "$(readlink -f "$0")")";

purge="";
for arg in "$@"; do
	if [ "$arg" = "-purge" ]; then
		purge="y";
	else
		echo "Unknown argument! argument: $arg";
		exit 1;
	fi
done

if [ "$(id -u)" != "0" ]; then
	echo "This uninstaller must run as root. Try:";
	echo "  sudo \"$destinationPath/vhuninstall\" $*";
	exit 1;
fi

# Nothing is deleted but an install's folder, which has its publish.json
if [ "$destinationPath" = "/" ] || [ ! -f "$destinationPath/publish.json" ]; then
	echo "Not an install of $productName: $destinationPath";
	exit 1;
fi

echo "Uninstalling $productName...";

# The services first, while the launcher their stop runs is still there
for unit in "$assemblyName.service" "${assemblyName}Updater.service"; do
	if [ -f "/etc/systemd/system/$unit" ]; then
		systemctl disable --now "$unit" >/dev/null 2>&1;
		rm -f "/etc/systemd/system/$unit";
	fi
done
systemctl daemon-reload;
systemctl reset-failed "$assemblyName.service" "${assemblyName}Updater.service" >/dev/null 2>&1;

# What still runs from the install - a server started by hand, a client's open windows - ends by
# SIGTERM, on which the server removes its tun and NAT rules as on its stop command
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
rm -rf "/run/$assemblyName";

# The client's command on the PATH, only while it is this install's
if [ "$(readlink -f "/usr/local/bin/$launcher")" = "$destinationPath/$launcher" ]; then
	rm -f "/usr/local/bin/$launcher";
fi

# The client's menu entry and its icon
desktopEntry="/usr/share/applications/$assemblyName.desktop";
if [ -f "$desktopEntry" ]; then
	rm -f "$desktopEntry" "/usr/share/icons/hicolor/256x256/apps/$assemblyName.png";
	update-desktop-database /usr/share/applications >/dev/null 2>&1;
	if command -v gtk-update-icon-cache >/dev/null 2>&1; then
		gtk-update-icon-cache -f -t /usr/share/icons/hicolor >/dev/null 2>&1;
	fi
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
	echo "  To remove them too: sudo rm -rf \"$destinationPath\"";
fi
exit 0;
} # the group opened at the top
