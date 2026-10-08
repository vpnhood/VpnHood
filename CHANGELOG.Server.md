# Latest

* Fix: VpnHoodServer shuts down cleanly on systemctl stop, docker stop, Ctrl+C or a dropped terminal, as on its stop command: it sends its last usage report and removes its tun and NAT rules, which a signal used to leave behind, and a stop no longer closes a self-hosted server's sessions for good. The stop command now returns once the server has exited, and a stop sent while the server is starting is no longer lost.
* Update: On Linux, an installed VpnHoodServer stays down after its stop command; a crash or a failed start still restarts it, and `systemctl restart` restarts it by hand. A crash at start-up is now in its journal (`journalctl -u VpnHoodServer`).
* Fix: VpnHoodServer exits with code 1 when a command fails; it used to exit 0 after printing the error. A broken NLog.config no longer silences the server: it logs to its console and to storage/logs/server.log, and names the problem.
* Fix: VpnHoodServer never bills a client's traffic twice: reports to the access server no longer cross, and when one fails its traffic is dropped rather than sent again, so the client has it free.
* Fix: VpnHoodServer reports a client's disconnect to the access server once, even when another report crosses it, and does not report back a session the access server closed itself.
* Fix: A rewarded ad's reward is no longer undone by a usage report that crossed it on the server, which could end the session early.
* Fix: VpnHoodServer no longer fails a connection when two clients connect at the same moment, nor a client that comes back while its idle session is being removed.
* Fix: VpnHoodServer removes the saved records of sessions whose clients never came back; they used to pile up in its internal/sessions folder.
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
