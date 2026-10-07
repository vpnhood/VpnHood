#!/bin/bash
# The desktop client's installer. The server's install.sh is next to this one and is NOT this:
# a server is a daemon and nothing else, while a client is three things out of one binary - a
# root service that owns the tunnel, a window that a logged-in person opens, and commands they
# type - and the difference is almost entirely in what gets installed around the binary.
#
# What this writes:
#   /opt/<name>/bin/<version>/   the self-contained build, the old ones removed at an update
#   /opt/<name>/storage/         settings.json, profiles, the log  (root's alone, 700)
#   /usr/local/bin/<launcher>    so the commands can be typed from anywhere
#   /etc/systemd/system/<name>.service           the root service, headless
#   /usr/share/applications/<name>.desktop       the window, in the app grid
#   /usr/share/icons/hicolor/256x256/apps/<name>.png   its icon
#
# Ubuntu and Debian are what it is tested on; anything with systemd and apt, or systemd and the
# GUI libraries already present, will do.

# One group, which bash reads whole before it runs any of it: the updater of earlier releases pipes
# this script into bash, and a download cut short would otherwise run as far as it got.
{
echo "$(productNameParam) Installation for Linux";

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
autostart="";
quiet="";
withDesktop="";

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
	echo "Unknown argument! argument: $lastArg";
	exit 1;
fi;
lastArg=$arg;
done;

# Root, said here. Everything below writes to /opt, /etc and /usr, and a run that discovers that
# one directory at a time leaves half an install behind.
if [ "$(id -u)" != "0" ]; then
	echo "This installer must run as root. Try:";
	echo "  sudo bash $0 $*";
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

# Is there a desktop on this machine? Only a graphical default target gets the window, its icon
# and its libraries; a server install stops at the service and the commands. Asked before the
# prompts so the answer can be shown rather than demanded.
if [ "$withDesktop" == "" ]; then
	defaultTarget=$(systemctl get-default 2>/dev/null);
	if [ "$defaultTarget" == "graphical.target" ]; then
		withDesktop="y";
	else
		withDesktop="n";
	fi
fi

# User interaction
if [ "$quiet" != "y" ]; then
	if [ "$autostart" == "" ]; then
		read -p "Start the VpnHood service at boot (Y/n)? " autostart;
		if [ -z "$autostart" ]; then
			autostart="y";
		fi;
	fi;
fi;
if [ "$autostart" == "" ]; then
	autostart="y";
fi

# ------------------------------------------------------------------
# Dependencies
# ------------------------------------------------------------------
# The build is self-contained - it carries .NET and Skia - so the only things missing on a bare
# machine are the C libraries Avalonia opens by name at runtime, and they are needed only where
# there is a window to draw. A failure here is a warning, not an exit: a machine with them
# already, or one that is not Debian at all, must still finish installing.
if [ "$withDesktop" == "y" ]; then
	missing="";
	for lib in libx11-6 libice6 libsm6 libfontconfig1 xdg-utils; do
		if ! dpkg -s "$lib" >/dev/null 2>&1; then
			missing="$missing $lib";
		fi
	done

	if [ -n "$missing" ]; then
		echo "Installing the libraries the window needs:$missing";
		if command -v apt-get >/dev/null 2>&1; then
			DEBIAN_FRONTEND=noninteractive apt-get update -qq;
			# --no-install-recommends, and it matters: xdg-utils RECOMMENDS a terminal emulator and
			# an X utility set, so without this a VPN client quietly installs tilix, x11-utils,
			# cpp and libllvm on somebody's machine. Only what the window actually opens by name.
			# shellcheck disable=SC2086
			if ! DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends $missing; then
				echo "WARNING: Could not install:$missing";
				echo "WARNING: The commands and the service will work; the window may not open.";
			fi
		else
			echo "WARNING: apt-get was not found. Install these yourself before opening the window:$missing";
		fi
	fi
fi

# MsQuic, for the QUIC channel. Optional everywhere: without it the client uses TCP.
msquic_url="$releaseUrl/VpnHoodServer-linux-msquic.sh"
if ! msquic_script=$(wget -qO- "$msquic_url"); then
	echo "WARNING: Could not download MsQuic installer from: $msquic_url"
	echo "WARNING: wget failed. Skipping MsQuic installation."
elif ! bash <(printf '%s' "$msquic_script"); then
	echo "WARNING: MsQuic installation failed. The client will use TCP."
fi

# ------------------------------------------------------------------
# The package
# ------------------------------------------------------------------
# Into the install's own folder, not the current one: the updater's is /, where every update used to
# leave the package. A crashed run's leftovers go first.
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
# there; one already there is complete and is kept, a running window's files with it
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
# the package the updater of earlier releases left in its working folder, /
rm -f "/$assemblyName-linux.tar.gz";

# The service goes down for the switch and comes back at the end - which is also what makes this
# script an in-place upgrade. Not before: a package that does not extract leaves it running.
if systemctl is-active --quiet "$assemblyName.service" 2>/dev/null; then
	echo "Stopping the running service...";
	systemctl stop "$assemblyName.service";
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
replace_file "$infoDir/$launcher" "$destinationPath/$launcher" 755;
replace_file "$infoDir/publish.json" "$destinationPath/publish.json" 644;
chmod +x "$binDir/$assemblyName";

# Old versions go only here, at an update: one stays while it is among the three newest, was
# installed in the last 30 days or a process still runs from it, such as a window left open across
# updates. Version folders at the top, from the layout before bin, count too; a folder whose name is
# not a whole version is left alone.
function remove_old_versions() {
	local versionDirs=() versionDir newest running versionPattern='^v[0-9]+(\.[0-9]+){1,3}(-.+)?$';
	for versionDir in "$destinationPath"/bin/v[0-9]* "$destinationPath"/v[0-9]*; do
		if [ -d "$versionDir" ] && [[ $(basename "$versionDir") =~ $versionPattern ]]; then
			versionDirs+=("$(readlink -f "$versionDir")");
		fi
	done
	newest=$(for versionDir in "${versionDirs[@]}"; do basename "$versionDir"; done | sort -V -r | head -3);
	running=$(readlink /proc/[0-9]*/exe 2>/dev/null);
	for versionDir in "${versionDirs[@]}"; do
		if grep -qxF "$(basename "$versionDir")" <<< "$newest" || [ -n "$(find "$versionDir" -maxdepth 0 -mtime -30)" ] ||
			grep -qF "$versionDir/" <<< "$running"; then
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
# SIGTERM stops the daemon, which disconnects the tunnel before it exits.
echo "Writing the $assemblyName service...";
service="
[Unit]
Description=$productName
After=network.target

[Service]
Type=simple
ExecStart=$destinationPath/$launcher daemon
TimeoutStartSec=0
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
";
echo "$service" > "/etc/systemd/system/$assemblyName.service";

echo "Writing the $assemblyName updater service...";
updaterService="
[Unit]
Description=$productName Updater
After=network.target

[Service]
Type=simple
ExecStart=$destinationPath/vhupdate
TimeoutStartSec=0
Restart=always
RestartSec=720min

[Install]
WantedBy=multi-user.target
";
echo "$updaterService" > "/etc/systemd/system/${assemblyName}Updater.service";

systemctl daemon-reload;

if [ "${autostart,,}" = "y" ]; then
	systemctl enable "$assemblyName.service";
	systemctl enable "${assemblyName}Updater.service";
	# Not from inside it (above): the unit's next run takes the unit file and the vhupdate written
	# here anyway.
	if [ "$insideUpdaterUnit" != "y" ]; then
		systemctl restart "${assemblyName}Updater.service";
	fi
else
	systemctl disable "$assemblyName.service" >/dev/null 2>&1;
	systemctl disable "${assemblyName}Updater.service" >/dev/null 2>&1;
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

	# The head's own icon, published beside the binary: AppIcon.png, the picture its Windows icon
	# holds at 256 px, placed at that size. Older installers put the UI's 60 px logo in 512x512, which
	# the dock blew up into a blur, so it goes. No icon, and the entry is still a working entry, with
	# the desktop's generic one.
	iconLine="";
	iconDir="/usr/share/icons/hicolor/256x256/apps";
	rm -f "/usr/share/icons/hicolor/512x512/apps/$assemblyName.png";
	if [ -f "$binDir/AppIcon.png" ] && mkdir -p "$iconDir" && cp -f "$binDir/AppIcon.png" "$iconDir/$assemblyName.png"; then
		iconLine="Icon=$assemblyName";
	fi
	if [ -z "$iconLine" ]; then
		echo "WARNING: Could not place the application icon; the menu entry will use a generic one.";
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
";
	echo "$desktopEntry" > "/usr/share/applications/$assemblyName.desktop";
	update-desktop-database /usr/share/applications >/dev/null 2>&1;
	if command -v gtk-update-icon-cache >/dev/null 2>&1; then
		gtk-update-icon-cache -f -t /usr/share/icons/hicolor >/dev/null 2>&1;
	fi
fi

# ------------------------------------------------------------------
echo "";
echo "$productName has been installed.";
if [ "$withDesktop" == "y" ]; then
	echo "  Open the window from the application menu, or run: $launcher";
fi
echo "  From a terminal:";
echo "    $launcher profile add <access-key>";
echo "    $launcher connect";
echo "    $launcher status";
echo "    $launcher --help";
} # the group opened at the top
