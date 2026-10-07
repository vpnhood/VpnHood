# -----------------------------------------------
# Install MsQuic Library
# -----------------------------------------------
install_msquic() {
    echo "Installing MsQuic library..."
    export DEBIAN_FRONTEND=noninteractive
    export NEEDRESTART_MODE=a

    if [ -f /etc/os-release ]; then
        . /etc/os-release
        distro=$ID
        distro_version=$VERSION_ID
    else
        echo "WARNING: Cannot detect Linux distribution. Skipping MsQuic installation."
        return 1
    fi

    _link_and_register_msquic() {
        local msquic_lib
        msquic_lib=$(find /usr/lib64 /usr/lib -name "libmsquic.so.2" 2>/dev/null | head -1)
        if [ -n "$msquic_lib" ]; then
            mkdir -p /usr/local/lib/msquic
            ln -sf "$msquic_lib" /usr/local/lib/msquic/libmsquic.so.2
            if command -v ldconfig >/dev/null 2>&1; then
                ldconfig
            fi
            echo "MsQuic installed: $msquic_lib"
        else
            echo "WARNING: libmsquic.so.2 not found after installation."
            return 1
        fi
    }

    # -----------------------------------------------
    # Attempt to resolve a working feed URL by walking
    # back through versions until one responds with 200.
    # e.g., 25.04 -> 24.10 -> 24.04 -> 22.04
    # -----------------------------------------------
    _resolve_deb_feed_url() {
        local distro="$1"
        local requested_version="$2"

        # Build a deduplicated candidate list:
        # start with the exact version, then known stable fallbacks
        local candidates=""
        candidates="$requested_version"

        if [ "$distro" = "ubuntu" ]; then
            # Append known LTS releases as fallbacks, oldest-last
            for fallback in "24.04" "22.04" "20.04"; do
                # Skip if it's already the requested version
                [ "$fallback" = "$requested_version" ] && continue
                candidates="$candidates $fallback"
            done
        elif [ "$distro" = "debian" ]; then
            for fallback in "12" "11" "10"; do
                [ "$fallback" = "$requested_version" ] && continue
                candidates="$candidates $fallback"
            done
        fi

        for ver in $candidates; do
            local url="https://packages.microsoft.com/config/${distro}/${ver}/packages-microsoft-prod.deb"
            echo "  Checking feed for ${distro} ${ver}..." >&2
            # Use --spider to probe without downloading
            if wget -q --spider "$url" 2>/dev/null; then
                echo "  Feed found for ${distro} ${ver}." >&2
                # Print the resolved version so the caller knows which one worked
                echo "$url|$ver"
                return 0
            fi
        done

        echo "WARNING: No compatible Microsoft package feed found for ${distro} (tried: ${candidates})." >&2
        return 1
    }

    # -----------------------------------------------
    # Same idea for RPM-based distros
    # -----------------------------------------------
    _resolve_rpm_feed_url() {
        local distro="$1"
        local requested_version="$2"

        local candidates="$requested_version"

        if [ "$distro" = "fedora" ]; then
            for fallback in "40" "39" "38"; do
                [ "$fallback" = "$requested_version" ] && continue
                candidates="$candidates $fallback"
            done
        else
            # RHEL / CentOS - major version only
            local major_ver
            major_ver=$(echo "$requested_version" | cut -d. -f1)
            candidates="$major_ver"
            for fallback in "9" "8" "7"; do
                [ "$fallback" = "$major_ver" ] && continue
                candidates="$candidates $fallback"
            done
        fi

        for ver in $candidates; do
            if [ "$distro" = "fedora" ]; then
                local url="https://packages.microsoft.com/config/fedora/${ver}/packages-microsoft-prod.rpm"
            else
                local url="https://packages.microsoft.com/config/rhel/${ver}/packages-microsoft-prod.rpm"
            fi
            echo "  Checking feed for ${distro} ${ver}..." >&2
            if wget -q --spider "$url" 2>/dev/null; then
                echo "  Feed found for ${distro} ${ver}." >&2
                echo "$url|$ver"
                return 0
            fi
        done

        echo "WARNING: No compatible Microsoft package feed found for ${distro} (tried: ${candidates})." >&2
        return 1
    }

    # -----------------------------------------------
    # Main install branches
    # -----------------------------------------------
    if [ "$distro" = "ubuntu" ] || [ "$distro" = "debian" ]; then

        local resolved
        if ! resolved=$(_resolve_deb_feed_url "$distro" "$distro_version"); then
            return 1
        fi

        # Split "url|resolved_version" returned by the probe function
        local feed_url resolved_version
        feed_url=$(echo "$resolved" | cut -d'|' -f1)
        resolved_version=$(echo "$resolved" | cut -d'|' -f2)

        if [ "$resolved_version" != "$distro_version" ]; then
            echo "NOTE: No feed for ${distro} ${distro_version}. Falling back to ${distro} ${resolved_version} feed (MsQuic is ABI-compatible)."
        fi

        local tmp_deb
        tmp_deb=$(mktemp /tmp/packages-microsoft-prod.XXXXXX.deb)

        # Tell apt to WAIT for the dpkg lock instead of failing. apt-get is fail-fast by default, and
        # on first boot the lock is usually held by unattended-upgrades/cloud-init - that race is what
        # makes a fresh install start the server permanently without QUIC. Setting this once as a
        # drop-in applies to every apt-get below, so we don't repeat a flag on each command.
        # (The feed package is installed via apt-get rather than `dpkg -i` so it honors this too -
        # a direct dpkg call does not read apt config.)
        # The drop-in is temporary: the RETURN trap removes it so we don't permanently change apt.
        local apt_lock_conf=/etc/apt/apt.conf.d/99-vpnhood-msquic
        mkdir -p /etc/apt/apt.conf.d
        echo 'DPkg::Lock::Timeout "60";' > "$apt_lock_conf"
        trap 'rm -f "$apt_lock_conf"' RETURN

        if ! wget -nv -O "$tmp_deb" "$feed_url"; then
            rm -f "$tmp_deb"
            echo "WARNING: Failed to download feed package from ${feed_url}."
            return 1
        fi

        # Microsoft's feed serves this install alone: one added here goes again below, so the machine
        # keeps no feed whose dotnet packages would mix with the distribution's own. One that was
        # there already stays. Every update runs this script again, which keeps libmsquic current.
        local feed_added=""
        if [ "$(dpkg-query -W -f='${db:Status-Status}' packages-microsoft-prod 2>/dev/null)" != "installed" ]; then
            feed_added="y"
        fi

        local installed=""
        if ! apt-get install -y "$tmp_deb"; then
            echo "WARNING: Failed to install Microsoft package feed."
        elif ! apt-get update; then
            echo "WARNING: apt-get update failed after adding Microsoft feed."
        elif ! apt-get install -y libmsquic; then
            echo "WARNING: libmsquic package installation failed."
        else
            installed="y"
        fi
        rm -f "$tmp_deb"

        if [ "$feed_added" = "y" ] && dpkg-query -W packages-microsoft-prod >/dev/null 2>&1 &&
            ! apt-get purge -y packages-microsoft-prod; then
            echo "WARNING: Could not remove the Microsoft package feed again."
        fi

        if [ "$installed" != "y" ]; then
            return 1
        fi
        _link_and_register_msquic

    elif [ "$distro" = "rhel" ] || [ "$distro" = "centos" ] || [ "$distro" = "fedora" ]; then

        local resolved
        if ! resolved=$(_resolve_rpm_feed_url "$distro" "$distro_version"); then
            return 1
        fi

        local feed_url resolved_version
        feed_url=$(echo "$resolved" | cut -d'|' -f1)
        resolved_version=$(echo "$resolved" | cut -d'|' -f2)

        if [ "$resolved_version" != "$distro_version" ]; then
            echo "NOTE: No feed for ${distro} ${distro_version}. Falling back to ${distro} ${resolved_version} feed (MsQuic is ABI-compatible)."
        fi

        # Microsoft's feed for this install alone, as on Debian above
        local feed_added=""
        if ! rpm -q packages-microsoft-prod >/dev/null 2>&1; then
            feed_added="y"
        fi
        rpm -Uvh "$feed_url" 2>/dev/null || true

        local package_manager
        package_manager=$(command -v dnf >/dev/null 2>&1 && echo dnf || echo yum)
        local installed=""
        if "$package_manager" install -y libmsquic; then
            installed="y"
        else
            echo "WARNING: libmsquic installation failed via $package_manager."
        fi

        if [ "$feed_added" = "y" ] && rpm -q packages-microsoft-prod >/dev/null 2>&1 &&
            ! rpm -e packages-microsoft-prod; then
            echo "WARNING: Could not remove the Microsoft package feed again."
        fi

        if [ "$installed" != "y" ]; then
            return 1
        fi
        _link_and_register_msquic

    else
        echo "WARNING: Unsupported distribution '${distro}' for MsQuic installation. Skipping."
        return 1
    fi
}

install_msquic
