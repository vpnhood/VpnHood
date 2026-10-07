#!/bin/bash
echo "Updating VpnHood Module for linux...";
# absolute: the install script gets this folder as its destination
curDir="$(dirname "$(readlink -f "$0")")";
localPublishInfoFile="$curDir/publish.json";

# -container: run by the container's supervisor, where there is no systemd. The install script then
# writes no units and starts nothing; the supervisor starts the new version.
container="";
for arg in "$@"; do
	if [ "$arg" = "-container" ]; then
		container="y";
	fi
done

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

# -------------------
# Check for Update
# -------------------

# load local publish info
localPublishInfoJson=`cat $localPublishInfoFile`;
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
    exit 0;
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
modeArg="-autostart";
if [ "$container" = "y" ]; then
    modeArg="-container";
fi
bash "$installScriptFile" -q -destination "$curDir" "$modeArg";
