#!/bin/bash
# The updater of a server installed by a user without root: install.sh beside it places it in the install's folder
# as vhupdate, and it runs as that user, by their updater service or cron line, or by hand. It installs the feed's
# latest release by that release's own user installer, which then starts the server again. Root's install has its
# own, pub/lib/vh-installer/linux/updater.sh.
assemblyName="$(assemblyNameParam)";
productName="$(productNameParam)";
curDir="$(dirname "$(readlink -f "$0")")";
echo "Updating $productName...";

# One update at a time, each reading the install only once the one before has ended: a run that waited finds the
# update done, at the version check below.
if command -v flock >/dev/null 2>&1 && exec 8>>"$curDir/.update.lock"; then
	flock 8;
fi

# -------------------
# Functions
# -------------------

# wget, else curl, each with a minute for its waits and three tries: a network that drops what it filters, rather
# than refusing it, would otherwise hold the download for some forty minutes - and the lock with it.
# download <url> <file>, - for standard output
function download() {
	if command -v wget >/dev/null 2>&1; then
		wget --timeout=60 --tries=3 -q -O "$2" "$1";
	else
		curl -fsSL --connect-timeout 60 --speed-limit 1 --speed-time 60 --retry 2 -o "$2" "$1";
	fi
}

function json_extract() {
	local string_regex='"([^"\]|\\.)*"';
	local number_regex='-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?';
	local value_regex="${string_regex}|${number_regex}|true|false|null";
	local pair_regex="\"$1\"[[:space:]]*:[[:space:]]*(${value_regex})";
	if [[ $2 =~ ${pair_regex} ]]; then
		sed 's/^"\|"$//g' <<< "${BASH_REMATCH[1]}";
	else
		return 1;
	fi
}

function version() {
	echo "$@" | awk -F. '{ printf("%d%03d%03d%03d\n", $1,$2,$3,$4); }';
}

# -------------------
# Check for Update
# -------------------
localPublishInfoJson=$(cat "$curDir/publish.json" 2>/dev/null);
if [ -z "$localPublishInfoJson" ]; then
	echo "Could not load the installed package information! Path: $curDir/publish.json";
	exit 1;
fi
localVersion=$(json_extract Version "$localPublishInfoJson");
localUpdateCode=$(json_extract UpdateCode "$localPublishInfoJson");
updateInfoUrl=$(json_extract UpdateInfoUrl "$localPublishInfoJson");

onlinePublishInfoJson=$(download "$updateInfoUrl" -);
if [ -z "$onlinePublishInfoJson" ]; then
	echo "Could not retrieve the latest package information! Url: $updateInfoUrl";
	exit 1;
fi
onlineVersion=$(json_extract Version "$onlinePublishInfoJson");
onlineUpdateCode=$(json_extract UpdateCode "$onlinePublishInfoJson");
onlinePackageUrl=$(json_extract PackageUrl "$onlinePublishInfoJson");

# The compatibility contract: a release that changed it cannot be installed over this one
if [ "$localUpdateCode" != "$onlineUpdateCode" ]; then
	echo "The installed version can not be updated. You need to update it manually!";
	exit 1;
fi

echo "Installed version: $localVersion";
echo "Latest version: $onlineVersion";
if [ "$(version "$localVersion")" -ge "$(version "$onlineVersion")" ]; then
	echo "The installed version is up to date.";
	exit 0;
fi

# The release's own user installer, beside its package, downloaded whole before it runs. -q alone: the install
# keeps its own choices, starting at boot and updating among them.
echo "Installing the latest version";
runDir=$(mktemp -d) || exit 1;
trap 'rm -rf "$runDir"' EXIT;
installerUrl="${onlinePackageUrl%/*}/$assemblyName-linux-user.sh";
if ! download "$installerUrl" "$runDir/install.sh"; then
	echo "Could not download the install script! Url: $installerUrl";
	exit 1;
fi
bash "$runDir/install.sh" -q -destination "$curDir";
