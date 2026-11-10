#!/bin/bash
# Sets the operating system's own updater to install the system's security fixes every night and to restart the
# machine then when one needs it, or takes that back. The server's installer downloads it from its own release and
# runs it in a bash of its own:
#   install-os-updates.sh status|on|off <name> [<title>]
# <name> names the files it writes, <title> says in them who set them up. status prints on or off where they can
# be set up, else "unsupported: <why>". on and off say what they did, and exit 1 when they could not.
# One group, which bash reads whole before it runs any of it: a download cut short runs nothing.
{
action="$1";
name="$2";
title="${3:-$2}";
if [[ ! "$action" =~ ^(status|on|off)$ ]] || [[ ! "$name" =~ ^[A-Za-z0-9._-]+$ ]]; then
	echo "Usage: install-os-updates.sh status|on|off <name> [<title>]";
	exit 1;
fi

# Our own files are the state, and the only ones written; beyond them, only the system's own update timers are
# switched on where they are off
aptConf="/etc/apt/apt.conf.d/90$name-os-updates";
aptTimer="/etc/systemd/system/apt-daily-upgrade.timer.d/90-$name-os-updates.conf";
dnfConf="/etc/dnf/$name-os-updates.conf";
dnfService="/etc/systemd/system/${name}OsUpdates.service";
dnfTimer="/etc/systemd/system/${name}OsUpdates.timer";
function is_on() {
	[ -f "$aptConf" ] || [ -f "$aptTimer" ] || [ -f "$dnfConf" ] || [ -f "$dnfService" ] || [ -f "$dnfTimer" ];
}

# apt where Debian or Ubuntu is in the system's lineage. dnf only in the Red Hat family from 9: its
# dnf-automatic restarts the machine, 8's does not; CentOS Stream publishes no security advisories, so its
# security-only run would install nothing; Fedora's dnf5 reads only the system-wide settings, never ours.
function detect() {
	local ids major;
	tool="";
	whyNot="";
	if [ ! -d /run/systemd/system ]; then
		whyNot="systemd is not running here";
		return;
	fi
	ids=" $(. /etc/os-release 2>/dev/null; echo "$ID $ID_LIKE") ";
	major=$(. /etc/os-release 2>/dev/null; echo "${VERSION_ID%%.*}");
	if command -v apt-get >/dev/null 2>&1 && [[ "$ids" =~ \ (debian|ubuntu)\  ]]; then
		tool="apt";
	elif command -v dnf >/dev/null 2>&1 && [[ "$ids" =~ \ (rhel|ol)\  ]] && [[ ! "$ids" =~ ^\ centos\  ]] &&
		[[ "$major" =~ ^[0-9]+$ ]] && [ "$major" -ge 9 ] && dnf --version 2>/dev/null | head -n 1 | grep -q '^4\.'; then
		tool="dnf";
	else
		whyNot="they can be set up on Debian, Ubuntu and the Red Hat family from 9, not on this system";
	fi
}

# Written whole, through a new file; an earlier one is kept aside, so a failure can put it back
function write_file() {
	local file="$1" temp="";
	if [ -e "$file" ]; then
		if ! cp -p "$file" "$backup/$(basename "$file")"; then
			error="could not keep a copy of $file";
			return 1;
		fi
		replaced+=("$file");
	else
		created+=("$file");
	fi
	if ! mkdir -p "$(dirname "$file")" ||
		! temp=$(mktemp "$(dirname "$(dirname "$file")")/.$(basename "$file").XXXXXX") ||
		! printf '%s' "$2" > "$temp" || ! chmod 644 "$temp" || ! mv -f "$temp" "$file"; then
		[ -n "$temp" ] && rm -f "$temp";
		error="could not write $file";
		return 1;
	fi
}
function undo_files() {
	local file;
	if [[ " ${created[*]} " == *" $dnfTimer "* ]]; then
		systemctl disable --now "${name}OsUpdates.timer" >/dev/null 2>&1;
	fi
	for file in "${created[@]}"; do
		rm -f "$file";
	done
	for file in "${replaced[@]}"; do
		cp -p "$backup/$(basename "$file")" "$file";
	done
	rmdir "$(dirname "$aptTimer")" 2>/dev/null;
	systemctl daemon-reload;
	systemctl try-restart apt-daily-upgrade.timer "${name}OsUpdates.timer" 2>/dev/null;
}

# The time, picked once in 02:00-04:29 on the machine's clock, so a farm's servers seldom restart together
function run_time() {
	local runTime minute;
	runTime=$(grep -hsoE '^OnCalendar=\*-\*-\* ([01][0-9]|2[0-3]):[0-5][0-9]$' "$aptTimer" "$dnfTimer" |
		head -n 1 | grep -o '[0-9:]*$');
	if [ -z "$runTime" ]; then
		minute=$((RANDOM % 150));
		runTime=$(printf '%02d:%02d' $((2 + minute / 60)) $((minute % 60)));
	fi
	echo "$runTime";
}

# The system's daily upgrade moves from the morning to the night, where the restarts it may bring - the
# machine's, or a service's after a library fix - do least harm; no catch-up after a boot, which would bring
# them by day. What it installs stays the distribution's choice: its security fixes, unless the person chose
# more. The machine restarts half an hour later, the next night when the upgrade ran by day. Debian's and
# Ubuntu's servers come with unattended-upgrades and the timers; a minimal system gets the first.
function enable_apt() {
	local runTime="$1" rebootMinute rebootTime unit;
	rebootMinute=$(( (10#${runTime%:*} * 60 + 10#${runTime#*:} + 30) % 1440 ));
	rebootTime=$(printf '%02d:%02d' $((rebootMinute / 60)) $((rebootMinute % 60)));
	if ! dpkg-query -W -f='${Status}' unattended-upgrades 2>/dev/null | grep -q " installed$"; then
		echo "Installing unattended-upgrades, the system's own updater...";
		NEEDRESTART_SUSPEND=1 DEBIAN_FRONTEND=noninteractive apt-get update -qq -o DPkg::Lock::Timeout=300;
		if ! NEEDRESTART_SUSPEND=1 DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends \
			-o DPkg::Lock::Timeout=300 -o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold \
			unattended-upgrades; then
			error="could not install unattended-upgrades";
			return 1;
		fi
	fi
	# a person's mask stays: then it is theirs to lift
	for unit in apt-daily.timer apt-daily-upgrade.timer apt-daily.service apt-daily-upgrade.service; do
		if ! systemctl cat "$unit" >/dev/null 2>&1 || [[ "$(systemctl is-enabled "$unit" 2>/dev/null)" == masked* ]]; then
			error="the system's $unit is missing or masked";
			return 1;
		fi
	done
	write_file "$aptConf" "// Set up by $title: the system's unattended-upgrades installs its updates every night and
// restarts the machine when one needs it. To stop it, delete this file and $aptTimer.
APT::Periodic::Enable \"1\";
APT::Periodic::Update-Package-Lists \"1\";
APT::Periodic::Unattended-Upgrade \"1\";
Unattended-Upgrade::Automatic-Reboot \"true\";
Unattended-Upgrade::Automatic-Reboot-Time \"$rebootTime\";
" || return 1;
	write_file "$aptTimer" "# Set up by $title: the system's daily upgrade runs at night, with no catch-up after a boot.
# To stop it, delete this file and $aptConf.
[Timer]
OnCalendar=
OnCalendar=*-*-* $runTime
RandomizedDelaySec=0
Persistent=false
" || return 1;
	if ! systemctl daemon-reload; then
		error="systemd could not reload its units";
		return 1;
	fi
	if ! systemctl enable --now apt-daily.timer apt-daily-upgrade.timer >/dev/null 2>&1 ||
		! systemctl restart apt-daily-upgrade.timer; then
		error="could not start the system's apt-daily timers";
		return 1;
	fi
	result="the system installs its updates every night at $runTime and restarts at $rebootTime when one needs it";
}

# Our own timer runs the system's dnf-automatic with our own settings, so its units and its automatic.conf, the
# person's, stay as they are. It restarts the machine five minutes after a run that needs it.
function enable_dnf() {
	local runTime="$1";
	if ! rpm -q dnf-automatic >/dev/null 2>&1; then
		echo "Installing dnf-automatic, the system's own updater...";
		if ! dnf install -y -q dnf-automatic; then
			error="could not install dnf-automatic";
			return 1;
		fi
	fi
	if ! grep -qs "when-needed" /usr/lib/python3*/site-packages/dnf/automatic/main.py; then
		error="this system's dnf-automatic cannot restart the machine";
		return 1;
	fi
	write_file "$dnfConf" "# Set up by $title, for ${name}OsUpdates.service: the system's security fixes, and a restart
# when one needs it. To stop it, disable ${name}OsUpdates.timer.
[commands]
upgrade_type = security
random_sleep = 0
network_online_timeout = 60
download_updates = yes
apply_updates = yes
reboot = when-needed
reboot_command = \"shutdown -r +5 'Restarting after security fixes'\"

[emitters]
emit_via = stdio

[base]
debuglevel = 1
" || return 1;
	write_file "$dnfService" "[Unit]
Description=$title: the operating system's security fixes
Wants=network-online.target
After=network-online.target

[Service]
Type=oneshot
ExecStart=/usr/bin/dnf-automatic \"$dnfConf\" --installupdates
" || return 1;
	write_file "$dnfTimer" "[Unit]
Description=$title: the operating system's security fixes, every night

[Timer]
OnCalendar=*-*-* $runTime

[Install]
WantedBy=timers.target
" || return 1;
	if ! systemctl daemon-reload; then
		error="systemd could not reload its units";
		return 1;
	fi
	if ! systemctl enable "${name}OsUpdates.timer" >/dev/null 2>&1 ||
		! systemctl restart "${name}OsUpdates.timer"; then
		error="could not start ${name}OsUpdates.timer";
		return 1;
	fi
	result="the system installs its security fixes every night at $runTime and restarts five minutes after the run when one needs it";
}

# A failure puts back what this run changed; the package it installed stays
function turn_on() {
	local runTime;
	detect;
	if [ -z "$tool" ]; then
		echo "WARNING: Operating system updates were not turned on: $whyNot.";
		return 1;
	fi
	echo "Turning on operating system updates...";
	runTime=$(run_time);
	created=();
	replaced=();
	error="";
	result="";
	if ! backup=$(mktemp -d); then
		echo "WARNING: Operating system updates were not turned on: no room for a temporary folder.";
		return 1;
	fi
	if "enable_$tool" "$runTime"; then
		rm -rf "$backup";
		echo "Operating system updates are on: $result (this machine's clock).";
		return 0;
	fi
	undo_files;
	rm -rf "$backup";
	echo "WARNING: Operating system updates were not turned on: $error.";
	return 1;
}

# Off takes back only what this script set up; the system's own update settings stay as they are
function turn_off() {
	local aptUnit="";
	if ! is_on; then
		return 0;
	fi
	[ -f "$aptTimer" ] && aptUnit="apt-daily-upgrade.timer";
	if [ -f "$dnfTimer" ] && ! systemctl disable --now "${name}OsUpdates.timer" >/dev/null 2>&1; then
		echo "WARNING: Operating system updates are still on: could not stop ${name}OsUpdates.timer.";
		return 1;
	fi
	if ! rm -f "$aptConf" "$aptTimer" "$dnfConf" "$dnfService" "$dnfTimer"; then
		echo "WARNING: Could not remove all of the operating system update settings $title set up.";
		return 1;
	fi
	rmdir "$(dirname "$aptTimer")" 2>/dev/null;
	# the system's daily upgrade back at its own time
	if ! systemctl daemon-reload || { [ -n "$aptUnit" ] && ! systemctl try-restart "$aptUnit"; }; then
		echo "WARNING: The operating system update settings $title set up are removed, but systemd still runs";
		echo "         them until it reloads its units: sudo systemctl daemon-reload";
		return 1;
	fi
	echo "Operating system updates set up by $title are off. The system's own update settings stay as they are.";
}

if [ "$action" = "status" ]; then
	detect;
	if [ -n "$whyNot" ]; then
		echo "unsupported: $whyNot";
	elif is_on; then
		echo "on";
	else
		echo "off";
	fi
	exit 0;
fi
# a lock of their own, not waited for, keeps two runs from writing them at once
if command -v flock >/dev/null 2>&1 && { ! exec 8>>"/run/$name-os-updates.lock" || ! flock -n 8; }; then
	echo "WARNING: Operating system updates were not changed: another run is changing them.";
	exit 1;
fi
if ! "turn_$action"; then
	exit 1;
fi
exit 0;
} # the group opened at the top
