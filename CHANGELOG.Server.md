# Latest

* Feature: VpnHoodServer limits each session's DNS over UDP, by default to 200 packets a second (`MaxUdpDnsPacketsPerSecond` in the session options), and drops the excess, so one client can no longer flood a DNS server through it and get the server's address reported for abuse. Its log names the session that reached the limit.
* Update: The install scripts no longer take -autostart and -autoupdate, the default since v9.0.856, nor -restBaseUrl, -restAuthorization and -secret, the 2023 names of -httpBaseUrl, -httpAuthorization and -managementSecret. A server still on a release before v9.0.856 no longer updates itself; run its install command again.
* Fix: The Docker install script asks about installing Docker only when Docker or its compose plugin is missing, with Enter for yes, and takes Y and yes as yes; it used to ask every time, and took anything but a lowercase y as no. Finding no Docker when it is not to install it, it stops at once and says so; it used to fail at the container's start.
* Improve: The install scripts and the uninstaller list their options with -help, --help or -h; the Docker install script had no help. Every option takes one dash or two: --noautoupdate is -noautoupdate.
* Fix: An install started while VpnHoodServer updates itself waits for the update to finish, so its choices stand; the update could undo them, turning updates back on after -noautoupdate. The uninstaller waits too.

# v9.0.859

* Update: UDP has a new protocol; VpnHoodServer offers it to clients that have it, and older clients keep the old one for now.
* Update: A self-hosted VpnHoodServer now serves UDP by default, on the addresses it listens on for TCP and on a port it picks at its first start and keeps; its log names that port at each start. Allow the port for UDP in the server's firewall and the hosting provider's, as an app set to UDP gets no traffic through a blocked port, or turn UDP off with `"UdpEndPoints": []`.
* Improve: VpnHoodServer stops with one line and exit code 1 when its settings cannot be read - appsettings.json, naming the file and where its JSON breaks, or VH_APPSETTINGS - rather than crashing with a stack trace and dumping core at each restart.
* Fix: VpnHoodServer finishes a stop within 30 seconds, so a slow stop is no longer killed by systemd or Docker before it removes the server's network adapter and NAT rules.
* Fix: The Docker install script makes the server's storage folder, with its settings, certificates and tokens, readable by root alone, as a native install does; it was open to every account on the host. A quote or a backslash in a value given to it no longer breaks the settings file.
* Fix: A prerelease's Docker install script installs that prerelease's image; it used to install the latest stable one.
* Improve: In Docker, VpnHoodServer leaves the TCP congestion control to the host and says so, rather than warning at each start that it could not set it.
* Improve: A self-hosted VpnHoodServer no longer warns at each start that its local settings overwrote its remote configuration; the settings file it ships held only defaults there.

# v9.0.856

* Fix: A connection to VpnHoodServer no longer ends two days after it began while still in use. The server now reports an active session's traffic to its access manager every sync interval (20 minutes by default), not only once the session goes quiet, and a self-hosted server's session times out 48 hours after its last use.
* Fix: A restart or an update of a self-hosted VpnHoodServer no longer ends a connected device's session as suppressed by another session, which could happen when that device had connected before in the last two days.
* Update: VpnHoodServer passes each new session's analytics choice, which the client now sends in its hello, to the access manager (`SessionRequestEx.AllowAnonymousTracker`), so an access manager can report only the sessions that allow it. A client before this sends none, which reads as no.
* Fix: VpnHoodServer's tracking log writes no destination address when destination tracking is off; when it is on, it writes a token in place of each address, even with the log anonymizer off. Its ICMP lines follow the ICMP switch; they used to follow the UDP one.
* Fix: VpnHoodServer's logs redact NAT64 addresses, which carry the IPv4 address they reach: those in 64:ff9b::/96 as that address, those in 64:ff9b:1::/48 whole. They used to be written as they were.
* Update: VpnHoodServer's console no longer prints the session lines, which carry the client IP; they stay in server.log and sessions.log.
* Update: VpnHoodServer's server.log starts a new file every day as well as at 10 MB, and the shipped NLog.config keeps every archive 28 days, so no line is older than 30 days when it goes.
* Fix: When its NLog.config cannot be used, VpnHoodServer's fallback log leaves out the tracking and session lines, which carry client IPs; that log never rolls, so each start begins it anew.
* Update: The Docker compose file caps the container's console log at three files of 10 MB.
* Fix: VpnHoodServer shuts down cleanly on systemctl stop, docker stop, Ctrl+C or a dropped terminal, as on its stop command: it sends its last usage report and removes its tun and NAT rules, which a signal used to leave behind, and a stop no longer closes a self-hosted server's sessions for good. The stop command now returns once the server has exited, and a stop sent while the server is starting is no longer lost.
* Update: On Linux, an installed VpnHoodServer stays down after its stop command; a crash or a failed start still restarts it, and `systemctl restart` restarts it by hand. A crash at start-up is now in its journal (`journalctl -u VpnHoodServer`).
* Fix: VpnHoodServer exits with code 1 when a command fails; it used to exit 0 after printing the error. A broken NLog.config no longer silences the server: it logs to its console and to storage/logs/server.log, and names the problem.
* Fix: VpnHoodServer never bills a client's traffic twice: reports to the access server no longer cross, and when one fails its traffic is dropped rather than sent again, so the client has it free.
* Fix: VpnHoodServer reports a client's disconnect to the access server once, even when another report crosses it, and does not report back a session the access server closed itself.
* Fix: A rewarded ad's reward is no longer undone by a usage report that crossed it on the server, which could end the session early.
* Fix: VpnHoodServer no longer fails a connection when two clients connect at the same moment, nor a client that comes back while its idle session is being removed.
* Fix: VpnHoodServer removes the saved records of sessions whose clients never came back; they used to pile up in its internal/sessions folder.
* Update: The installer asks about updating apart from starting at boot, and both are on by default: `-noautostart` and `-noautoupdate` turn them off, `-autostart` is no longer needed, and an update keeps the choices made at install. Every earlier option still works, `-restBaseUrl`, `-restAuthorization` and `-secret` again among them; `-help` lists them.
* Fix: On Windows, an install or update unpacks the new version before it stops the server, and starts the server again if anything fails after the stop, so a failed download or update no longer leaves the server down. The updater's own scheduled task is never stopped or replaced by an install.
* Update: The installer notes an active firewall - firewalld or ufw on Linux, Windows Firewall on Windows - since clients reach the server only through the ports it allows; opening them stays the administrator's.
* Feature: On Linux the installer sets up MsQuic, so the server can take QUIC, on more distributions: openSUSE and SLES, Arch-based ones such as EndeavourOS and Manjaro, and those built on Ubuntu, Debian or RHEL, such as Rocky Linux and AlmaLinux. They used to get none.
* Feature: VpnHoodServer has an uninstaller: `sudo /opt/VpnHoodServer/vhuninstall` on Linux; on Windows it is listed in Installed apps, or run `vhuninstall.ps1` in its folder. Its settings and certificates stay for a later install unless you add `-purge`. An install gets it with its next update.
* Fix: A self-hosted VpnHoodServer no longer logs an error at start for each session that closed in the last day; it skips them, as they are not to be recovered.
* Fix: Session timeouts and cleanup no longer drift with the server's time zone or a daylight-saving change.
* Fix: On Linux, the server no longer runs proxy-only when a firewall or Docker holds the iptables lock as it starts; it waits up to 5 seconds for the lock.
* Fix: On Linux, a tun left down by a killed server is cleared at the next start; the server used to refuse it and run proxy-only, with an error suggesting another VPN held it.
* Fix: On Linux, a stop during an address change no longer leaves a tun or a NAT rule behind.
* Update: The installed server keeps its versions in the install's bin folder, and an update removes old ones: the three newest stay, and any installed in the last 30 days or still running
* Fix: Installing or updating no longer leaves the downloaded package behind, and removes the one earlier updates left: VpnHoodServer-linux.tar.gz in / on Linux, VpnHoodServer-win.zip in C:\Windows\System32 on Windows
* Update: The Docker server updates itself, with no Watchtower container: it checks for a new release at start and every 12 hours and installs it in the container, as an installed server does; VH_AUTO_UPDATE=0 turns that off. A release that needs a new image is not installed; the log says "Pull the latest image!". Watchtower, archived, could no longer run on a recent Docker and never updated the server. A server installed with the Docker install script: run the script again, which takes the new image and removes the VpnHoodUpdater container. A container you created yourself: pull the latest image and create it again.
* Fix: The Docker install script's -install-docker works on Debian and on Ubuntu's and Debian's derivatives, and leaves an installed Docker alone; it used Ubuntu's repository and sudo. The script needs root. A compose file that fails to download no longer replaces the one in place, -composeFile is no longer ignored, and settings given with -httpBaseUrl reach a server that is already running.
* Fix: The install scripts stop at an unknown option or one missing its value; a last one used to be ignored.
* Fix: On Linux, the installed server's files belong to root; they kept the user id of the machine that built them, so an account with that id could replace what the server runs as root. The storage folder is root's alone, so appsettings.json, with the management secret, is no longer readable by every account, and a quote or a backslash in a value given to the install script no longer breaks that file.
* Fix: On Linux, the install script stops at once when not run as root, and removes Microsoft's package feed again after installing MsQuic, unless the feed was there before. After a boot the updater's first check waits for the network; it used to fail and wait 12 hours.
* Fix: With no token in its store, VpnHoodServer names the command that creates one as it is run here, such as /opt/VpnHoodServer/vhserver gen; it said dotnet VpnHoodServer.dll gen.
* Fix: On Windows 10 and 11, the installed server updates itself; its updater task stopped at Windows' default script policy on every run. Windows Server was not affected. A server installed before this updates once by hand: run its vhupdate.ps1, or install the new release.
* Fix: The session log shows a new session's virtual IP and its OS each under its own name; the two were swapped.
* Feature: VpnHoodServer takes its settings from the VH_APPSETTINGS environment variable when it is set: the content of an appsettings.json, which then replaces the file, for a Docker-only service with no file to edit. The log names where the settings came from. In a container, run gen and the other token commands with docker exec, as docker exec VpnHoodServer /app/vhsupervisor gen, so they read the same settings.
* Feature: On Windows Server, VpnHoodServer forwards its clients' traffic through its own adapter and Windows' NAT, as it does on Linux, so clients may send TCP as packets, not only through the TCP proxy. Windows' NAT carries IPv4 only, so IPv6 TCP still goes through the TCP proxy; an older client's IPv6 TCP does not work there, and its device falls back to IPv4. Where the adapter cannot start, the server runs as before, with the TCP proxy only.
* Fix: When its adapter fails to start again after a network change, VpnHoodServer brings it back by itself and offers new clients the TCP proxy meanwhile; it used to stay without the adapter until restarted, and clients that sent TCP as packets lost their TCP. It also notices an adapter that the system disabled or took down under it, and brings it back the same way.
* Fix: On Linux, an installed VpnHoodServer waits for the network at boot (network-online.target, where the system's wait-online service is enabled). It could start before the default route existed, find no network adapter, and then run without its tun (TCP by the TCP proxy only) until restarted by hand.
* Update: VpnHoodServer holds a speed-limited session to its limit in one place for all its traffic: after a pause about 10 MB goes at full speed, then packets wait in a short queue, about a quarter second of the limit, where they used to wait seconds; a session idle for a long time no longer gets unlimited speed for its saved allowance. A client's upload is checked at 1.2 times its limit, with a 12 MB burst.
* Fix: The Docker server no longer logs errors at start about a network interface it could not find: it says once that it runs as a proxy, since a container has no TUN device.
* Fix: On Linux the installer installs ICU where it is missing, as on Debian 13's cloud image: VpnHoodServer did not start without it.
* Fix: VpnHoodServer starts on openSUSE Tumbleweed; it stopped at start on a comment line in /etc/os-release.

# v8.1.849

* Fix: UDP channel stops working for all sessions after a closing session's packet arrives late

# v8.1.837

* Fix: QUIC server stops after some errors

# v8.0.815

* Feature: Support QUIC on Windows and Linux
* Fix: Exceeding the 7-day threshold error

# v7.9.813

* Feature: Pause zombie servers for a while to save resources
* Feature: Traffic throttling

# v7.8.801

* Fix: IPv6 UDP listener may stop working after a while

# v7.7.796

* Fix: UDP protocol on some networks and devices
* Update: Detect when UDP protocol is not available due to reverse proxy

# v7.7.795

* Fix: Auto public IP resolution returning 104.18.0.0

# v7.7.792

* Fix: Reconnection required after network failure when using the TCP protocol

# v7.5.777

* Update: Upgrade to .NET 10
* Update: Update HTTP01 handling for valid SSL certs

# v7.3.766

Fix: Set DNS on some Linux distros

# v7.3.762

* Fix: Access to local network when IncludeLocalNetwork is enabled

# v7.3.751

* Fix: Negative speed issue

# v7.3.742

* Feature: Extract Client IP from reserve proxies
* Feature: Add AllowTcpProxy option
* Feature: Add AllowTcpPacket option
* Fix: Block Stream Proxy requests by NetFilter

# v7.3.739

* Feature: Handle UserReview recommendation

# v7.2.732

* Fix: Reconnecting especially when using torrent clients
* Update: Tune memory usage

# v7.2.729

* Fix: Memory Leak!

# v7.1.725

* Feature: Support reserve proxies like Cloudflare

# v7.1.724

* Improve: Speed and stability
* Update: Improve session management
* Update: Move libraries to .NET 8.0

# v7.1.716

* Fix: IPv6
* Fix: Memory leak
* Improve: Drastically improve performance and stability
* Improve: Drastically reduce memory usage

# v6.0.696

* Improve: Watching media streaming
* Improve: Performance and stability
* Fix: Network Isolation
* Fix: Some Android devices could not open websites after connecting

# v6.0.689

* Fix: Calculate UDP checksum and DNS response

# v6.0.680

* Fix: Pause server while configuring swap file on Linux
* Feature: Use TunAdapter on Linux, improve performance

# v6.0.656

* Update: Improve virtual IP allocation
* Fix: Some Android devices could not open website after connect

# v5.1.647

* Fix: Ping IPv6 

# v5.1.642

* Update: Improve ping performance

# v5.0.629

* Update: Move releases repo from https://github.com/vpnhood/VpnHood/releases

# v5.0.629

Old log: https://github.com/vpnhood/VpnHood/blob/main/CHANGELOG.md
