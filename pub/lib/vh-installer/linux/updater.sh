#!/bin/bash
echo "Updating VpnHood Module for linux...";
# absolute: the install script gets this folder as its destination
curDir="$(dirname "$(readlink -f "$0")")";
localPublishInfoFile="$curDir/publish.json";
updatesDir="$curDir/storage/updates";

# -container: run by the container's supervisor, where there is no systemd. The install script then
# writes no units and starts nothing; the supervisor starts the new version.
# -packageFile <file>: the package the app downloaded, installed rather than downloaded again: the notice's Install,
# or the desktop client's service handing back a download after a disconnect. The checks below still decide.
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

# One updater at a time - the timer's, the service's, the notice's - each reading the install only once the one
# before has ended: a run that waited finds the update done, at the version check below. The install script keeps
# a lock of its own, for an install run by hand.
if command -v flock >/dev/null 2>&1 && exec 8>>"/run/$(basename "$curDir")-update.lock"; then
	flock 8;
fi

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

# The files in the updates folder but the one given: a release overtaken, one installed, a download a run before this
# one left unfinished - one updater runs at a time. Never the app's download under way (.part).
function remove_downloads() {
	local download;
	for download in "$updatesDir"/*; do
		if [ -f "$download" ] && [ "$download" != "$1" ] && [[ "$download" != *.part ]]; then
			rm -f "$download";
		fi
	done
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

# load online publish info
onlinePublishInfoJson=$( wget -qO- $localUpdateInfoUrl);
onlineVersion=$(json_extract Version "$onlinePublishInfoJson");
onlineUpdateCode=$(json_extract UpdateCode "$onlinePublishInfoJson");
onlineInstallScriptUrl=$(json_extract InstallScriptUrl "$onlinePublishInfoJson");
onlinePackageUrl=$(json_extract PackageUrl "$onlinePublishInfoJson");

# Check is latest version available
if [ "$onlinePublishInfoJson" == "" ]; then
    echo "Could not retrieve the latest package information! Url: $localUpdateInfoUrl";
    exit 1;
fi

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
echo "Latest version: $onlineVersion";
if [ $(version "$localVersion") -ge $(version "$onlineVersion") ]; then
    echo "The installed version is up to date.";
    [ "$waitForIdle" = "y" ] && remove_downloads "";
    exit 0;
fi

if [ "$waitForIdle" = "y" ] && [ "$packageFile" = "" ]; then
	# https alone, as the app's own download takes it, or a test feed on this machine
	if [[ "$onlinePackageUrl" != https://* && "$onlinePackageUrl" != http://127.0.0.1[:/]* && "$onlinePackageUrl" != http://localhost[:/]* ]]; then
		echo "The package is not on https: $onlinePackageUrl";
		exit 1;
	fi

	# downloaded whole under the app's name for it (DesktopAppUpdaterProvider), into a file of this run's that
	# becomes the package by rename
	packageFile="$updatesDir/$onlineVersion-$(basename "$onlinePackageUrl")";
	remove_downloads "$packageFile";
	if [ ! -f "$packageFile" ]; then
		echo "Downloading $onlineVersion...";
		mkdir -p "$updatesDir" || exit 1;
		downloadFile=$(mktemp "$updatesDir/.download.XXXXXX") || exit 1;
		if ! wget -nv -O "$downloadFile" "$onlinePackageUrl" || ! mv -f "$downloadFile" "$packageFile"; then
			echo "Could not download the update! Url: $onlinePackageUrl";
			rm -f "$downloadFile";
			exit 1;
		fi
	fi

	# what the client's service says of its VPN: 0 and "connected", 3 and its state, any other code no answer
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
fi

# Install the new version. The script is downloaded whole before it runs: run from a pipe, a broken
# download would run as far as it got.
echo "Installing the latest version";
installScriptFile=$(mktemp) || exit 1;
trap 'rm -f "$installScriptFile"' EXIT;
if ! wget -qO "$installScriptFile" "$onlineInstallScriptUrl"; then
    echo "Could not download the install script! Url: $onlineInstallScriptUrl";
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
