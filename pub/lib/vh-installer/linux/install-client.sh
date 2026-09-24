#!/bin/bash
# The desktop client's installer. The server's install.sh is next to this one and is NOT this:
# a server is a daemon and nothing else, while a client is three things out of one binary - a
# root service that owns the tunnel, a window that a logged-in person opens, and commands they
# type - and the difference is almost entirely in what gets installed around the binary.
#
# What this writes:
#   /opt/<name>/<version>/       the self-contained build
#   /opt/<name>/storage/         settings.json, profiles, the log  (root's)
#   /usr/local/bin/<launcher>    so the commands can be typed from anywhere
#   /etc/systemd/system/<name>.service           the root service, headless
#   /usr/share/applications/<name>.desktop       the window, in the app grid
#
# Ubuntu and Debian are what it is tested on; anything with systemd and apt, or systemd and the
# GUI libraries already present, will do.

echo "$(productNameParam) Installation for Linux";

# Default arguments
releaseUrl="$(releaseUrlParam)";
packageUrl="$(packageUrlParam)";
versionTag="$(versionTagParam)";
assemblyName="$(assemblyNameParam)";
productName="$(productNameParam)";
launcher="$(launcherNameParam)";
logoAssetPath="$(logoAssetPathParam)";

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
binDir="$destinationPath/$versionTag";

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
	for lib in libx11-6 libice6 libsm6 libfontconfig1 unzip xdg-utils; do
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
if [ "$packageFile" = "" ]; then
	echo "Downloading $productName...";
	packageFile="$assemblyName-linux.tar.gz";
	wget -nv -O "$packageFile" "$packageUrl";
	if [ $? != 0 ]; then
		echo "Could not download $packageUrl";
		exit 1;
	fi
fi

# A running service holds the binary it is executing, so it goes down before the files move and
# comes back at the end - which is also what makes this script an in-place upgrade.
if systemctl is-active --quiet "$assemblyName.service" 2>/dev/null; then
	echo "Stopping the running service...";
	systemctl stop "$assemblyName.service";
fi

echo "Extracting to $destinationPath";
mkdir -p "$destinationPath";
tar -xzf "$packageFile" -C "$destinationPath"
if [ $? != 0 ]; then
	echo "Could not extract $packageFile";
	exit 1;
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

# The storage the service owns. Made here rather than on first run so an advanced user has a
# folder to drop a settings.json into before anything has started.
mkdir -p "$destinationPath/storage";

# On the PATH, so "vhclient status" is a command and not a path to remember.
ln -sf "$destinationPath/$launcher" "/usr/local/bin/$launcher";

# ------------------------------------------------------------------
# The service
# ------------------------------------------------------------------
# "daemon", not the bare binary: the bare binary now opens a window, and a unit that asks for one
# on a machine with no display restarts forever without ever saying why.
echo "Writing the $assemblyName service...";
service="
[Unit]
Description=$productName
After=network.target

[Service]
Type=simple
ExecStart=$destinationPath/$launcher daemon
ExecStop=$destinationPath/$launcher stop
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

# And waited for. systemctl returns once systemd has STARTED the unit, but the service publishes
# the address its commands dial a few seconds later, when its listener has bound. Without this the
# closing message below invites somebody to run a command that is about to fail on a service which
# is coming up perfectly.
echo -n "Waiting for the service";
for _ in $(seq 1 40); do
	if [ -f "$destinationPath/storage/daemon.json" ]; then
		break;
	fi
	echo -n ".";
	sleep 1;
done
echo "";
if [ ! -f "$destinationPath/storage/daemon.json" ]; then
	echo "WARNING: The service was started but has not answered yet.";
	echo "WARNING: Check it with: journalctl -u $assemblyName -n 50";
fi

# ------------------------------------------------------------------
# The window
# ------------------------------------------------------------------
if [ "$withDesktop" == "y" ]; then
	echo "Adding $productName to the application menu...";

	# The icon comes out of the UI's own asset store - the same picture the window and a paired
	# phone's page draw - because this repo ships no second copy of it. No unzip, no icon: the
	# entry is still a working entry, with the desktop's generic one.
	iconLine="";
	iconDir="/usr/share/icons/hicolor/512x512/apps";
	if command -v unzip >/dev/null 2>&1 && [ -f "$binDir/assets/ui.zip" ]; then
		mkdir -p "$iconDir";
		if unzip -o -j -q "$binDir/assets/ui.zip" "$logoAssetPath" -d "$iconDir" 2>/dev/null; then
			mv -f "$iconDir/$(basename "$logoAssetPath")" "$iconDir/$assemblyName.png";
			iconLine="Icon=$assemblyName";
		fi
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
