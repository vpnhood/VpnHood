#!/bin/bash
# readlink, not dirname alone: the client installer puts a symlink to this script on the PATH
# (/usr/local/bin/vhclient), and $0 is then the symlink - whose folder holds no publish.json.
curDir="$(dirname "$(readlink -f "$0")")";
publishInfoFile="$curDir/publish.json";

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

# read publish.json
publishInfoJson=`cat $publishInfoFile`;
exeFileR=$(json_extract ExeFile "$publishInfoJson");
exeFile="$curDir/$exeFileR";
# The installer has already done this as root. Here it is a best effort for a build run from a
# folder nobody installed, and must not make a normal user's launch fail.
[ -x "$exeFile" ] || chmod +x "$exeFile" 2>/dev/null;

# The binary cannot know what a person typed to get here - "vhclient" is on the PATH and
# "VpnHoodClient" is not - so the hints it prints are told (LinuxCliPaths.LauncherNameVariable).
export VH_LAUNCHER_NAME="$(basename "$0")";

# Executing Module. exec, so the binary takes this process over: under the systemd unit the daemon
# is then the unit's main process - systemd's SIGTERM and the exit status are its own - and no
# shell is left reading this file, which an update replaces.
exec "$exeFile" "$@";