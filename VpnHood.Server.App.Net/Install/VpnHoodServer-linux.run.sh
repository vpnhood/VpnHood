#!/bin/bash
curDir="$(dirname "$0")";
exeFile="$curDir/{exeFileParam}";
chmod +x "$exeFile";

# Executing VpnHoodServer
"$exeFile" "$@";