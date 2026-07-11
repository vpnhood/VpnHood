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
echo "Executing VpnHood! $arch_raw installer ..."

# Download the script content first
script_content=$(wget -qO- "$script_url") || {
    echo "Error: Failed to download installation script from $script_url" >&2
    exit 1
}

# Check if empty
if [ -z "$script_content" ]; then
    echo "Error: Downloaded script is empty." >&2
    exit 1
fi

# Execute it with all arguments
bash -c "$script_content" -- "$@" || {
    echo "Error: Installation script failed." >&2
    exit 1
}
