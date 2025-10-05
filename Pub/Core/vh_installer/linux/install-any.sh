#!/bin/bash

# find cpu architecture
arch_raw=$(uname -m)

if [ "$arch_raw" = "x86_64" ]; then
    script_url="$installerUrl_x64"
elif [ "$arch_raw" = "aarch64" ]; then
    script_url="$installerUrl_arm64"
else
    echo "Error: Unsupported CPU architecture: $arch_raw" >&2
    exit 1
fi

# Pass all arguments to the downloaded script
echo "Executing VpnHood! $arch_raw installer ...";
bash <( wget -qO- "$script_url" ) "$@" || {
    echo "Error: Installation script failed." >&2
    exit 1
}

echo "VpnHood! CLIENT Linux (Beta) should be installed. There is no command-line at the moment.";
echo "You need to open your browser and navigate to http://127.0.0.1:9090/";
