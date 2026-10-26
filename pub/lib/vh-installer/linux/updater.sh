#!/bin/bash
echo "Updating VpnHood Module for linux...";
# absolute: the install script gets this folder as its destination
curDir="$(dirname "$(readlink -f "$0")")";
localPublishInfoFile="$curDir/publish.json";
updatesDir="$curDir/storage/updates";

# -container: run by the container's supervisor, where there is no systemd. The install script then
# writes no units and starts nothing; the supervisor starts the new version.
# -packageFile <file>: the package the app downloaded, installed rather than downloaded again: the notice's Install,
# or the desktop client's service handing back a download after a disconnect. It describes itself: the checks below
# read its release's publish.json, and the install runs the install script it carries, so nothing is read from the
# server - which a filtered network may let through only in the tunnel, down again after a disconnect. Carried in the
# package, not beside the download: the service takes any file in the updates folder named by a version for one.
# -waitForIdle: the desktop client's runs from its timer. The package is downloaded first, at any time, into the
# storage's updates folder, where the app finds it too, and installed only while the client's VPN is idle - or when
# its service does not answer, as a broken one would not, so that a broken release still gets the next one; else
# the download waits there, and the service hands it back after the next disconnect.
container="";
packageFile="";
waitForIdle="";
lastArg="";
for arg in "$@"; do
	if [ "$lastArg" = "-packageFile" ]; then
		packageFile="$arg";
	elif [ "$arg" = "-container" ]; then
		container="y";
	elif [ "$arg" = "-waitForIdle" ]; then
		waitForIdle="y";
	fi
	lastArg="$arg";
done

# The desktop client's own runs (-waitForIdle, no -packageFile): one that cannot reach the feed or a file it names
# leaves update.missed in the install's folder, for the service to run the updater again once its VPN connects -
# through the tunnel, where a filtered network may let it through - and one that reaches them takes it away.
missedFile="";
if [ "$waitForIdle" = "y" ] && [ "$packageFile" = "" ]; then
	missedFile="$curDir/update.missed";
fi

# One updater at a time - the timer's, the service's, the notice's - each reading the install only once the one
# before has ended: a run that waited finds the update done, at the version check below. The install script keeps
# a lock of its own, for an install run by hand.
if command -v flock >/dev/null 2>&1 && exec 8>>"/run/$(basename "$curDir")-update.lock"; then
	flock 8;
fi

# A minute for each of wget's waits, and three tries, which also resume a cut download: by default a network that
# drops what it filters, rather than refusing it, holds a wget for some forty minutes - and the lock with it.
wgetLimits=(--timeout=60 --tries=3);

# this run's files: a package's own, or the install script downloaded
runDir=$(mktemp -d) || exit 1;
trap 'rm -rf "$runDir"' EXIT;

# -------------------
# Functions
# -------------------
function json_extract() {
  local key=$1
  local json=$2

  local string_regex='"([^"\]|\\.)*"'
  local number_regex='-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?'
  local value_regex="${string_regex}|${number_regex}|true|false|null"
  local pair_regex="\"${key}\"[[:space:]]*:[[:space:]]*(${value_regex})"

  if [[ ${json} =~ ${pair_regex} ]]; then
    echo $(sed 's/^"\|"$//g' <<< "${BASH_REMATCH[1]}")
  else
    return 1
  fi
}

function version {
    echo "$@" | awk -F. '{ printf("%d%03d%03d%03d\n", $1,$2,$3,$4); }';
}

function missed() {
	if [ "$missedFile" != "" ]; then touch "$missedFile"; fi
}

function reached() {
	if [ "$missedFile" != "" ]; then rm -f "$missedFile"; fi
}

# The files in the updates folder but the ones given: a release overtaken, one installed, a download of another one
# that a run before this one left cut - one updater runs at a time. Never the app's download (.part), its own to keep.
function remove_downloads() {
	local download keep;
	for download in "$updatesDir"/*; do
		if [ ! -f "$download" ] || [[ "$download" == *.part ]]; then continue; fi
		for keep in "$@"; do
			if [ "$download" = "$keep" ]; then continue 2; fi
		done
		rm -f "$download";
	done
}

# The package's own release files into this run's folder: its publish.json, and the install script it carries, which
# an older package does not. Whole first, by gzip's own check: a cut one could hold half a script. One that does not
# read goes, as a cut download would, for the next run to download again.
function read_package() {
	gzip -t "$packageFile" 2>/dev/null &&
		tar -xzf "$packageFile" -C "$runDir" --strip-components=2 --wildcards \
			'*/publish_info/publish.json' '*/publish_info/install.sh' 2>/dev/null;
	if [ ! -s "$runDir/publish.json" ]; then
		echo "Could not read the package! File: $packageFile";
		if [[ "$packageFile" == "$updatesDir/"* ]]; then rm -f "$packageFile"; fi
		exit 1;
	fi
}

# -------------------
# Check for Update
# -------------------

# load local publish info
localPublishInfoJson=`cat "$localPublishInfoFile"`;
localVersion=$(json_extract Version "$localPublishInfoJson");
localUpdateCode=$(json_extract UpdateCode "$localPublishInfoJson");
localUpdateInfoUrl=$(json_extract UpdateInfoUrl "$localPublishInfoJson");

# Check is latest version available
if [ "$localPublishInfoJson" == "" ]; then
    echo "Could not load the installed package information! Path: $localPublishInfoFile";
    exit 1;
fi

# load the release's publish info: a package given describes itself; else the feed names the latest. A package given
# reads no feed, though a release taken back after its download then still installs (unless a run has read the feed
# since, which removes it): the feed first would help only on open networks, and cost every filtered install the
# feed's timeout
if [ "$packageFile" != "" ]; then
	read_package;
	onlinePublishInfoJson=$(cat "$runDir/publish.json");
else
	onlinePublishInfoJson=$(wget "${wgetLimits[@]}" -qO- "$localUpdateInfoUrl");
	if [ "$onlinePublishInfoJson" == "" ]; then
		echo "Could not retrieve the latest package information! Url: $localUpdateInfoUrl";
		missed;
		exit 1;
	fi
	reached;
fi
onlineVersion=$(json_extract Version "$onlinePublishInfoJson");
onlineUpdateCode=$(json_extract UpdateCode "$onlinePublishInfoJson");
onlineInstallScriptUrl=$(json_extract InstallScriptUrl "$onlinePublishInfoJson");
onlinePackageUrl=$(json_extract PackageUrl "$onlinePublishInfoJson");

# Compare the update code
if [ "$localUpdateCode" != "$onlineUpdateCode" ]; then
    if [ "$container" = "y" ]; then
        echo "The installed version can not be updated. Pull the latest image!";
    else
        echo "The installed version can not be updated. You need to update it manually!";
    fi
    exit 1;
fi

# Compare Version
echo "Installed version: $localVersion";
if [ "$packageFile" != "" ]; then echo "Package version: $onlineVersion"; else echo "Latest version: $onlineVersion"; fi
if [ $(version "$localVersion") -ge $(version "$onlineVersion") ]; then
    echo "The installed version is up to date.";
    [ "$waitForIdle" = "y" ] && remove_downloads "";
    exit 0;
fi

if [ "$waitForIdle" = "y" ] && [ "$packageFile" = "" ]; then
	# https alone, as the app's own download takes it (HTTPS is the check: DesktopAppUpdaterProvider), or a test feed on
	# this machine
	if [[ "$onlinePackageUrl" != https://* && "$onlinePackageUrl" != http://127.0.0.1[:/]* && "$onlinePackageUrl" != http://localhost[:/]* ]]; then
		echo "The package is not on https: $onlinePackageUrl";
		exit 1;
	fi

	# downloaded under the app's name for it (DesktopAppUpdaterProvider), into a file that becomes the package by
	# rename once whole. A cut download stays for the next run to continue (-c), or to start over where the server
	# ignores ranges: one per release, its name no version, so the app takes it for no package.
	packageFile="$updatesDir/$onlineVersion-$(basename "$onlinePackageUrl")";
	downloadFile="$updatesDir/download-$(basename "$packageFile")";
	remove_downloads "$packageFile" "$downloadFile";
	if [ ! -f "$packageFile" ]; then
		echo "Downloading $onlineVersion...";
		mkdir -p "$updatesDir" || exit 1;
		if ! wget "${wgetLimits[@]}" -nv -c -O "$downloadFile" "$onlinePackageUrl" || ! mv -f "$downloadFile" "$packageFile"; then
			echo "Could not download the update! Url: $onlinePackageUrl";
			missed;
			exit 1;
		fi
	fi

	# what the client's service says of its VPN: 0 and "connected", 3 and its state, any other code no answer. A connect
	# made in the seconds between this answer and the install's stop is dropped at the stop: the service's own
	# hand-back, which waits for the VPN to stay idle (IdleUpdateInstaller) and refuses connects meanwhile, has no gap
	exeFile=$(json_extract ExeFile "$localPublishInfoJson");
	stateJson=$("$curDir/$exeFile" status --json 2>/dev/null);
	stateCode=$?;
	connectionState=$(json_extract connectionState "$stateJson");
	if [ $stateCode -eq 0 ] || { [ $stateCode -eq 3 ] && [ "$connectionState" != "None" ]; }; then
		echo "The VPN is in use ($connectionState): $onlineVersion is installed after it disconnects.";
		exit 0;
	fi
fi

packageArgs=();
if [ "$packageFile" != "" ]; then
	packageArgs=(-packageFile "$packageFile");
	# this run's own download, read only now
	[ -s "$runDir/publish.json" ] || read_package;
fi

# Install the new version, by the install script its package carries: its own release's. A run with no package - the
# server's, whose install script downloads it - or an older one downloads the release's script, whole before it runs:
# run from a pipe, a broken download would run as far as it got.
echo "Installing the latest version";
installScriptFile="$runDir/install.sh";
if [ ! -s "$installScriptFile" ] && ! wget "${wgetLimits[@]}" -qO "$installScriptFile" "$onlineInstallScriptUrl"; then
    echo "Could not download the install script! Url: $onlineInstallScriptUrl";
    missed;
    exit 1;
fi
# -q alone: the install keeps its own choices, starting at boot and updating among them
if [ "$container" = "y" ]; then
    bash "$installScriptFile" -q -destination "$curDir" -container "${packageArgs[@]}";
else
    bash "$installScriptFile" -q -destination "$curDir" "${packageArgs[@]}";
fi
installCode=$?;

# A download the install could not take - spliced, cut short - goes, and the next run downloads it again whole
if [ $installCode -ne 0 ] && [[ "$packageFile" == "$updatesDir/"* ]]; then
	rm -f "$packageFile";
fi
exit $installCode;
