#!/bin/bash
# The container's service manager, its PID 1: what systemd does for a native install. It runs the
# server and starts it again 10 seconds after it exits on its own, whatever its exit code: it never
# exits itself but on a stop, as some platforms restart no container that exits cleanly, and some
# reset its files when they do. It runs vhupdate at start and every 12 hours, as the native updater
# unit does, beside the server, and starts the version vhupdate installed; it passes a stop on to the
# server and exits with it. The update logic stays in vhupdate and the release's install script, so a
# server that fails to run still gets the release that fixes it. VH_AUTO_UPDATE=0 turns the updates
# off, for an operator who pins a version. Shipped in the image only: no update rewrites the script
# that runs as PID 1.
appDir="$(dirname "$(readlink -f "$0")")";

# A command, such as gen, runs once, unsupervised.
if [ $# -gt 0 ]; then
	exec "$appDir/vhserver" "$@";
fi

serverPid="";
updaterPid="";
exeFileBeforeUpdate="";
stopping="";

# Docker's stop reaches PID 1 alone; the server gets it from here.
function on_stop() {
	stopping="y";
	if [ -n "$serverPid" ]; then kill -TERM "$serverPid" 2>/dev/null; fi
	if [ -n "$updaterPid" ]; then kill -TERM "$updaterPid" 2>/dev/null; fi
}
trap on_stop TERM INT;

# A stop that lands between a caller's check and this start finds no server to pass itself to, so the
# new one gets it here.
function start_server() {
	"$appDir/vhserver" &
	serverPid=$!;
	if [ "$stopping" = "y" ]; then kill -TERM "$serverPid" 2>/dev/null; fi
}

# SIGTERM, then SIGKILL after 30 seconds, as systemd stops a unit. Waits run in the background so a
# stop signal is handled at once rather than after the wait.
function stop_server() {
	kill -TERM "$serverPid" 2>/dev/null;
	local second;
	for second in $(seq 1 30); do
		kill -0 "$serverPid" 2>/dev/null || break;
		sleep 1 & wait $!;
	done
	kill -KILL "$serverPid" 2>/dev/null;
	wait "$serverPid" 2>/dev/null;
	serverPid="";
}

function exe_file() {
	grep -o '"ExeFile"[^,}]*' "$appDir/publish.json";
}

# vhupdate runs beside the server: a server that exits while an update takes its time, on apt or on
# the download, is started again meanwhile.
function start_update() {
	exeFileBeforeUpdate=$(exe_file);
	"$appDir/vhupdate" -container &
	updaterPid=$!;
}

# A new ExeFile in publish.json once vhupdate is done means it has installed a new version.
function finish_update() {
	wait "$updaterPid";
	updaterPid="";
	if [ "$stopping" = "y" ] || [ "$(exe_file)" = "$exeFileBeforeUpdate" ]; then return; fi
	echo "A new version is installed; restarting the server into it.";
	if [ -n "$serverPid" ]; then stop_server; fi
	if [ "$stopping" != "y" ]; then start_server; fi
}

start_server;
nextUpdate=0;
while [ "$stopping" != "y" ]; do
	if ! kill -0 "$serverPid" 2>/dev/null; then
		wait "$serverPid";
		echo "The server exited with code $?; starting it again in 10 seconds.";
		serverPid="";
		sleep 10 & wait $!;
		if [ "$stopping" = "y" ]; then break; fi
		start_server;
	fi

	if [ -n "$updaterPid" ]; then
		if ! kill -0 "$updaterPid" 2>/dev/null; then finish_update; fi
	elif [ "$VH_AUTO_UPDATE" != "0" ] && [ "$(date +%s)" -ge "$nextUpdate" ]; then
		nextUpdate=$(( $(date +%s) + 12 * 3600 ));
		start_update;
	fi

	sleep 5 & wait $!;
done

# A stop: the server has it already; leave with the server's own exit code.
if [ -n "$serverPid" ]; then
	wait "$serverPid";
	exit $?;
fi
exit 0;
