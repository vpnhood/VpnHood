#!/bin/bash
# The kill switch's way out (LinuxVpnKillSwitch): lifts its block with neither the service nor the app's binary, for
# a person whose app cannot open while the internet is blocked (owner, 2026-10-31: "we don't want the user to get
# stuck"). The block is an nftables table, its boot copy and the boot unit that loads the copy, under names every app
# built on VpnHood shares, so this lifts the block whichever app put it up, as the tunnel's holder does; the unit is
# written again at the next block. The installer places it beside the launcher as kill-switch-off; the app entry's
# right-click action, "<command> kill-switch off" while the service does not run, and the uninstaller run it. Root's
# alone: the block is the machine's.
tableName="vpnhood_kill_switch";
bootFile="/var/lib/vpnhood-kill-switch/block.nft";
bootUnit="vpnhood-kill-switch.service";

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

# one transaction, whose first line makes a missing table so the delete never fails
if [ -n "$nft" ] && ! printf 'table inet %s\ndelete table inet %s\n' "$tableName" "$tableName" | "$nft" -f -; then
	echo "Could not lift the kill switch's block.";
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

echo "The kill switch is not blocking the internet.";
exit 0;
