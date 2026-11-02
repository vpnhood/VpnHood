#!/bin/bash
# The kill switch's way out (LinuxVpnKillSwitch): lifts its block with neither the service nor the app's binary, for
# a person whose app cannot open while the internet is blocked, so they are never stuck behind it. The block is an
# nftables table, its boot copy and the boot unit that loads the copy, under names every app built on VpnHood
# shares, so this lifts the block whichever app put it up, as the tunnel's holder does; the unit is written again at
# the next block. The installer places it beside the launcher as kill-switch-off; the app entry's right-click action,
# "<command> kill-switch off" while the service does not run, and the uninstaller run it. Root's alone: the block is
# the machine's.
#   -notify <product>  the right-click action's: pkexec runs it from the person's desktop, where no line of output
#                      shows, so what it did goes to them as a notification too, as Windows' message box says it
tableName="vpnhood_kill_switch";
bootFile="/var/lib/vpnhood-kill-switch/block.nft";
bootUnit="vpnhood-kill-switch.service";
notifyApp="";
if [ "$1" = "-notify" ]; then
	notifyApp="${2:-Kill switch}";
fi

# the app's icon, which the installer names as its folder
iconName=$(basename "$(dirname "$(readlink -f "$0")")");

# A line, and with -notify a notification on the desktop of the person pkexec ran it for, through their session's
# bus; none where notify-send is missing.
function report() {
	echo "$1";
	local uid="${PKEXEC_UID:-$SUDO_UID}";
	if [ -z "$notifyApp" ] || [ -z "$uid" ] || ! command -v notify-send >/dev/null 2>&1; then
		return;
	fi
	runuser -u "$(id -nu "$uid")" -- env DBUS_SESSION_BUS_ADDRESS="unix:path=/run/user/$uid/bus" \
		notify-send -a "$notifyApp" -i "$iconName" "$1" >/dev/null 2>&1;
}

if [ "$(id -u)" != "0" ]; then
	echo "Only root can lift the kill switch's block. Try: sudo \"$(readlink -f "$0")\"";
	exit 1;
fi

# off a person's PATH, where the service finds it too
nft="";
for path in /usr/sbin/nft /sbin/nft /usr/bin/nft /bin/nft; do
	if [ -x "$path" ]; then
		nft="$path";
		break;
	fi
done

# whether there was a block, for what this says
wasBlocking="";
if [ -f "$bootFile" ] || { [ -n "$nft" ] && "$nft" list table inet "$tableName" >/dev/null 2>&1; }; then
	wasBlocking="y";
fi

# one transaction, whose first line makes a missing table so the delete never fails
if [ -n "$nft" ] && ! printf 'table inet %s\ndelete table inet %s\n' "$tableName" "$tableName" | "$nft" -f -; then
	report "The kill switch could not be turned off.";
	exit 1;
fi

# the boot copy and its unit, so a reboot does not put it back
rm -f "$bootFile";
rmdir "$(dirname "$bootFile")" 2>/dev/null;
if [ -f "/etc/systemd/system/$bootUnit" ]; then
	systemctl disable "$bootUnit" >/dev/null 2>&1;
	rm -f "/etc/systemd/system/$bootUnit";
	systemctl daemon-reload;
fi

if [ -n "$wasBlocking" ]; then
	report "The kill switch is off: the internet is no longer blocked.";
else
	report "The kill switch was not blocking the internet.";
fi
exit 0;
