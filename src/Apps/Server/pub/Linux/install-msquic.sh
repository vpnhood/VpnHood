# -----------------------------------------------
# Install MsQuic Library
# The app's version folder is the first argument: Arch's MsQuic goes there.
# -----------------------------------------------
install_msquic() {
    local app_dir="$1"
    # an optional library holds up no install for long; apt and dnf have timeouts of their own
    local wget_args=(--timeout=30 --tries=3)
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

    # A derivative takes the feed of the distribution it is built on, at that one's release: Linux
    # Mint, Pop!_OS and the like Ubuntu's, by their UBUNTU_CODENAME; Debian's, by debian_version;
    # Rocky, AlmaLinux and Oracle Linux RHEL's, by PLATFORM_ID (platform:el9); openSUSE Leap its own,
    # by its release. An unknown release, Tumbleweed's among them, takes the newest feed. Arch's are
    # found by their ID_LIKE below. The RPM feeds go by major release alone.
    case "$distro" in
        ubuntu | debian | fedora) ;;
        rhel | centos | sles) distro_version="${VERSION_ID%%.*}" ;;
        *)
            case " $ID_LIKE " in
                *" ubuntu "*)
                    distro="ubuntu"
                    case "$UBUNTU_CODENAME" in
                        focal) distro_version="20.04" ;;
                        jammy) distro_version="22.04" ;;
                        noble) distro_version="24.04" ;;
                        oracular) distro_version="24.10" ;;
                        plucky) distro_version="25.04" ;;
                        questing) distro_version="25.10" ;;
                        resolute) distro_version="26.04" ;;
                        *) distro_version="" ;;
                    esac
                    ;;
                *" debian "*)
                    distro="debian"
                    distro_version=$(cut -d. -f1 /etc/debian_version 2>/dev/null | grep -E '^[0-9]+$')
                    ;;
                *" rhel "* | *" centos "* | *" fedora "*)
                    case "$PLATFORM_ID" in
                        platform:el[0-9]*) distro="rhel" distro_version="${PLATFORM_ID#platform:el}" ;;
                        platform:f[0-9]*) distro="fedora" distro_version="${PLATFORM_ID#platform:f}" ;;
                    esac
                    ;;
                *" suse "* | *" opensuse "*)
                    distro="opensuse"
                    if [ "$ID" = "opensuse-leap" ]; then
                        distro_version="${VERSION_ID%%.*}"
                    else
                        distro_version=""
                    fi
                    ;;
            esac
            ;;
    esac

    # the package puts it where the loader looks. By glob: a minimal image has no find.
    _find_msquic() {
        local msquic_lib
        for msquic_lib in /usr/lib64/libmsquic.so.2 /usr/lib/libmsquic.so.2 /usr/lib/*/libmsquic.so.2; do
            if [ -e "$msquic_lib" ]; then
                echo "MsQuic installed: $msquic_lib"
                return 0
            fi
        done
        echo "WARNING: libmsquic.so.2 not found after installation."
        return 1
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
            for fallback in "26.04" "24.04" "22.04" "20.04"; do
                # Skip if it's already the requested version
                [ "$fallback" = "$requested_version" ] && continue
                candidates="$candidates $fallback"
            done
        elif [ "$distro" = "debian" ]; then
            for fallback in "13" "12" "11" "10"; do
                [ "$fallback" = "$requested_version" ] && continue
                candidates="$candidates $fallback"
            done
        fi

        for ver in $candidates; do
            local url="https://packages.microsoft.com/config/${distro}/${ver}/packages-microsoft-prod.deb"
            echo "  Checking feed for ${distro} ${ver}..." >&2
            # Use --spider to probe without downloading
            if wget -q --spider "${wget_args[@]}" "$url" 2>/dev/null; then
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
        elif [ "$distro" = "opensuse" ] || [ "$distro" = "sles" ]; then
            for fallback in "16" "15"; do
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

        local feed_distro="rhel"
        case "$distro" in
            fedora | opensuse | sles) feed_distro="$distro" ;;
        esac
        for ver in $candidates; do
            local url="https://packages.microsoft.com/config/${feed_distro}/${ver}/packages-microsoft-prod.rpm"
            echo "  Checking feed for ${distro} ${ver}..." >&2
            if wget -q --spider "${wget_args[@]}" "$url" 2>/dev/null; then
                echo "  Feed found for ${distro} ${ver}." >&2
                echo "$url|$ver"
                return 0
            fi
        done

        echo "WARNING: No compatible Microsoft package feed found for ${distro} (tried: ${candidates})." >&2
        return 1
    }

    # -----------------------------------------------
    # Arch has no libmsquic package: Microsoft's own build comes out of its RHEL 9 feed - it needs
    # glibc 2.34, OpenSSL 3's libcrypto, libnuma and no CPU newer than x86-64's baseline - into the
    # app's version folder, where .NET looks first. Nothing is left outside the app: each version gets
    # its own, newest copy, and the uninstaller takes it along.
    # -----------------------------------------------
    _install_msquic_on_arch() {
        local app_dir="$1"
        if [ ! -d "$app_dir" ]; then
            echo "WARNING: No app folder to put MsQuic in. Skipping MsQuic installation."
            return 1
        fi

        local rpm_arch
        case "$(uname -m)" in
            x86_64) rpm_arch="x86_64" ;;
            aarch64) rpm_arch="aarch64" ;;
            *)
                echo "WARNING: Microsoft builds no MsQuic for $(uname -m). Skipping MsQuic installation."
                return 1
                ;;
        esac

        # libmsquic needs libnuma. Without -y: refreshing the package lists for one package is a
        # partial upgrade, which Arch does not support.
        if ! pacman -Q numactl >/dev/null 2>&1 && ! pacman -S --noconfirm numactl; then
            echo "WARNING: MsQuic needs numactl, which could not be installed. Update the system (pacman -Syu), then install again."
            return 1
        fi

        local feed_url="https://packages.microsoft.com/rhel/9/prod/Packages/l"
        local rpm_file
        rpm_file=$(wget -qO- "${wget_args[@]}" "$feed_url/" |
            grep -oE "libmsquic-2\.[0-9.]+-[A-Za-z0-9._]+\.${rpm_arch}\.rpm" | sort -uV | tail -1)
        if [ -z "$rpm_file" ]; then
            echo "WARNING: Could not find libmsquic in ${feed_url}."
            return 1
        fi

        # libmsquic-2.6.2-1.x86_64.rpm holds usr/lib64/libmsquic.so.2.6.2
        local lib_version tmp_dir lib_file
        lib_version=$(echo "$rpm_file" | sed -E 's/^libmsquic-([0-9.]+)-.*/\1/')
        tmp_dir=$(mktemp -d) || return 1
        lib_file="$tmp_dir/usr/lib64/libmsquic.so.$lib_version"
        if ! wget -nv "${wget_args[@]}" -O "$tmp_dir/$rpm_file" "$feed_url/$rpm_file" ||
            ! bsdtar -xf "$tmp_dir/$rpm_file" -C "$tmp_dir" || [ ! -f "$lib_file" ]; then
            rm -rf "$tmp_dir"
            echo "WARNING: Could not extract libmsquic from ${feed_url}/${rpm_file}."
            return 1
        fi

        # .NET could not load it then, and would use TCP without a word
        if ldd "$lib_file" | grep -q "not found"; then
            echo "WARNING: MsQuic needs libraries this system lacks:"
            ldd "$lib_file" | grep "not found"
            rm -rf "$tmp_dir"
            return 1
        fi

        # by rename: an app already running from this folder keeps the file it loaded
        if ! install -m 755 "$lib_file" "$app_dir/.libmsquic.so.2.new" ||
            ! mv -f "$app_dir/.libmsquic.so.2.new" "$app_dir/libmsquic.so.2"; then
            rm -f "$app_dir/.libmsquic.so.2.new"
            rm -rf "$tmp_dir"
            echo "WARNING: Could not put MsQuic in ${app_dir}."
            return 1
        fi
        rm -rf "$tmp_dir"
        echo "MsQuic installed: $app_dir/libmsquic.so.2 ($rpm_file)"
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

        if [ -n "$distro_version" ] && [ "$resolved_version" != "$distro_version" ]; then
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

        if ! wget -nv "${wget_args[@]}" -O "$tmp_deb" "$feed_url"; then
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
        _find_msquic

    elif [ "$distro" = "rhel" ] || [ "$distro" = "centos" ] || [ "$distro" = "fedora" ] ||
        [ "$distro" = "opensuse" ] || [ "$distro" = "sles" ]; then

        local resolved
        if ! resolved=$(_resolve_rpm_feed_url "$distro" "$distro_version"); then
            return 1
        fi

        local feed_url resolved_version
        feed_url=$(echo "$resolved" | cut -d'|' -f1)
        resolved_version=$(echo "$resolved" | cut -d'|' -f2)

        if [ -n "$distro_version" ] && [ "$resolved_version" != "$distro_version" ]; then
            echo "NOTE: No feed for ${distro} ${distro_version}. Falling back to ${distro} ${resolved_version} feed (MsQuic is ABI-compatible)."
        fi

        # Microsoft's feed for this install alone, as on Debian above
        local feed_added=""
        if ! rpm -q packages-microsoft-prod >/dev/null 2>&1; then
            feed_added="y"
        fi
        # an unknown release, Tumbleweed's, takes the newest feed, whose package asks for that release
        local rpm_args=()
        if [ -z "$distro_version" ]; then
            rpm_args=(--nodeps)
        fi
        local tmp_rpm
        tmp_rpm=$(mktemp /tmp/packages-microsoft-prod.XXXXXX.rpm) || return 1
        if wget -nv "${wget_args[@]}" -O "$tmp_rpm" "$feed_url"; then
            rpm -Uvh "${rpm_args[@]}" "$tmp_rpm" 2>/dev/null || true
        else
            echo "WARNING: Failed to download feed package from ${feed_url}."
        fi
        rm -f "$tmp_rpm"

        # zypper fails at once on a lock another one holds (PackageKit's, at a first boot): it waits,
        # as apt does above
        local install_command
        if command -v zypper >/dev/null 2>&1; then
            install_command=(env ZYPP_LOCK_TIMEOUT=60 zypper --non-interactive --gpg-auto-import-keys install libmsquic)
        elif command -v dnf >/dev/null 2>&1; then
            install_command=(dnf install -y libmsquic)
        else
            install_command=(yum install -y libmsquic)
        fi
        local installed=""
        if "${install_command[@]}"; then
            installed="y"
        else
            echo "WARNING: libmsquic installation failed: ${install_command[*]}"
        fi

        if [ "$feed_added" = "y" ] && rpm -q packages-microsoft-prod >/dev/null 2>&1 &&
            ! rpm -e packages-microsoft-prod; then
            echo "WARNING: Could not remove the Microsoft package feed again."
        fi

        if [ "$installed" != "y" ]; then
            return 1
        fi
        _find_msquic

    # EndeavourOS, Manjaro and the like name Arch in ID_LIKE
    elif [ "$distro" = "arch" ] || [[ " $ID_LIKE " == *" arch "* ]]; then
        _install_msquic_on_arch "$app_dir"

    else
        echo "WARNING: Unsupported distribution '${distro}' for MsQuic installation. Skipping."
        return 1
    fi
}

install_msquic "$@"
