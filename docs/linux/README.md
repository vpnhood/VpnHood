# VpnHood on Linux

Ubuntu and Debian are what this is tested on; anything with systemd will do. One binary is
installed, and it is three things depending on the first word you give it:

| | What it is | Who runs it |
| --- | --- | --- |
| **The service** | Holds the VPN — the tunnel device, the routes, the DNS. Shows nothing. | root, by systemd |
| **The window** | The app, in a window. Holds no VPN of its own. | you, from the app menu |
| **The commands** | `connect`, `status`, `profile`… | you, from a terminal |

The window and the commands do not need root and never ask for it. They drive the service over
`http://127.0.0.1:4700`, which is the same API a paired phone's browser uses — so a headless
machine loses the window and nothing else.

Why the split: creating a tun device, editing the routing table and calling `resolvectl` all
require root, but a *window* must not run as root — a root GUI is refused outright by Wayland, and
it would open your links and write your files as root. So root is confined to the one process that
needs it.

---

## Install

```bash
# x64 and arm64, picked for you
sudo bash <(wget -qO- https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-linux.sh)
```

The installer must run as root and will say so rather than half-install. It:

- unpacks the build under `/opt/VpnHoodClient/<version>/`
- puts `vhclient` on your `PATH` (`/usr/local/bin/vhclient`)
- writes and starts `VpnHoodClient.service`, enabled at boot unless you answer no
- on a machine with a graphical target, adds **VpnHood! CLIENT** to the application menu and
  installs the libraries the window needs (`libx11-6`, `libice6`, `libsm6`, `libfontconfig1`,
  `unzip`, `xdg-utils`)

Flags worth knowing:

| Flag | Effect |
| --- | --- |
| `-q` | No questions. |
| `-autostart` | Start at boot (the default answer). |
| `-nodesktop` | Service and commands only: no menu entry, no GUI libraries. Use this on a server. |
| `-desktop` | Force the menu entry on, where the graphical target was not detected. |

For VpnHood! CONNECT the names are `vhconnect` and `VpnHoodConnect.service`. It ships with its
server built in and cannot be given another access key, so it has **no `profile` command and no
`--profile` option** — see [One profile, or many](#one-profile-or-many). Everything else is
identical.

### Uninstall

```bash
sudo systemctl disable --now VpnHoodClient.service VpnHoodClientUpdater.service
sudo rm -f /etc/systemd/system/VpnHoodClient*.service /usr/local/bin/vhclient \
           /usr/share/applications/VpnHoodClient.desktop
sudo systemctl daemon-reload
sudo rm -rf /opt/VpnHoodClient            # this deletes your settings and profiles too
rm -rf ~/.cache/VpnHoodClient
```

---

## Commands

`vhclient --help` lists them; `vhclient <command> --help` explains one. Every command that talks to
the service says so plainly when it is not running, and tells you how to start it.

A command run while the service is still coming up — right after an install, or right after a
reboot — waits for it rather than failing. Only a service that is not running at all is reported
immediately.

### First run

```bash
vhclient profile add 'vh://...'     # or a file: vhclient profile add ~/mykey.txt
vhclient connect
vhclient status
```

### Reference

| Command | What it does |
| --- | --- |
| `vhclient` | Opens the window. Starts the service first if it is not running. |
| `vhclient connect [--profile P] [--location L] [--no-wait]` | Connects, and waits until it has an answer: exit 0 means connected, not merely requested. |
| `vhclient disconnect` | Drops the tunnel. The service keeps running. |
| `vhclient status [--json] [--watch]` | What the app is doing. |
| `vhclient locations [--profile P] [--json]` | The server locations a profile offers. |
| `vhclient profile list [--json]` *(CLIENT only)* | Profiles on this device; `*` marks the one connect uses. |
| `vhclient profile add <key-or-file>` *(CLIENT only)* | Adds a profile from an access key. |
| `vhclient profile remove <profile>` *(CLIENT only)* | Removes one. |
| `vhclient profile set-default <profile>` *(CLIENT only)* | Chooses the profile `connect` uses when given none. |
| `vhclient service start\|stop\|restart` | Drives the systemd unit. Asks for your password. |
| `vhclient service status` | What systemd says. No password needed. |
| `vhclient service log [-f] [-n N]` | The service log, out of the journal. No password needed. |
| `vhclient stop` | Stops the running service directly. This is what the unit's `ExecStop` calls. |

### One profile, or many

Whether the `profile` command and the `--profile` option exist at all depends on the app, and it
follows one fact: can this app be given an access key?

| | `vhclient` (CLIENT) | `vhconnect` (CONNECT) |
| --- | --- | --- |
| Takes access keys | yes | no — one, built in |
| `profile` command | yes | **absent** |
| `--profile` on `connect` / `locations` | yes | **absent** |

On CONNECT there is exactly one profile and nothing to choose between, so those are not printed in
help, not parsed, and not quietly accepted. It is the same answer the app gives
`AppOptions.IsAddAccessKeySupported`; a head states it once, in `LinuxHeadParams`.

**Naming a profile.** On CLIENT, anywhere a command takes a profile you may give its id or its
name, and a name may be a prefix — `vhclient connect -p "VpnHood Sam"` is enough. A prefix that
matches two profiles is refused rather than guessed.

**Naming none.** `connect` and `locations` without `--profile` use, in order: the profile the app
is set to; failing that, the only profile there is. With several profiles and no default chosen
they stop and say so rather than pick one — `vhclient profile set-default <name>`. (Adding a key
does not make it the default; that is the app's behaviour, and the window's.)

**Naming a location.** Use the string in the first column of `vhclient locations`, for example
`US/Virginia` or `US/*`. `*/*` means fastest.

### Exit codes

Scripts can branch on these instead of parsing:

| Code | Meaning |
| --- | --- |
| `0` | Did what was asked. For `status`: connected. |
| `1` | Failed. The reason is on stderr. |
| `3` | `status` only: not connected. |
| `130` | Interrupted. |

### `--json`

`status`, `locations` and `profile list` take `--json`, and what they print is the app's own
object — the very fields the window reads, not a second format invented for the shell. A script
that outgrows the printed columns does not outgrow the command.

```bash
vhclient status --json | jq -r '.connectionState, .sessionStatus.sessionTraffic.received'
```

---

## Settings

Most settings are not commands, because a file you can read and diff beats twenty flags. The
service reads:

```
/opt/VpnHoodClient/storage/settings.json
```

It belongs to root, so edit it with `sudo`, then `sudo vhclient service restart`. Profiles, the log
and the app's other state live beside it in the same folder.

The window and the commands write nothing there. The window keeps its own extracted content under
`~/.cache/VpnHoodClient/`, per user — which is what lets it run without privilege.

---

## A headless machine

Install with `-nodesktop`, then drive it from the shell or from a unit of your own:

```bash
sudo bash <(wget -qO- https://.../VpnHoodClient-linux.sh) -q -autostart -nodesktop
sudo vhclient profile add /root/access-key.txt
vhclient connect --location US/Virginia
vhclient status
```

To connect on boot, add a unit that waits for the service and then asks it to connect:

```ini
[Unit]
Description=Connect VpnHood
After=VpnHoodClient.service
Requires=VpnHoodClient.service

[Service]
Type=oneshot
ExecStart=/usr/local/bin/vhclient connect

[Install]
WantedBy=multi-user.target
```

`connect` waits for a real answer and exits non-zero if the connection failed, so `Type=oneshot`
reports honestly.

---

## When something is wrong

| What you see | What it is |
| --- | --- |
| `The VpnHoodClient service is not running.` | Exactly that. `sudo vhclient service start`, then `vhclient service log`. |
| The menu entry does nothing | The window could not open. Run `vhclient ui` in a terminal to see why — usually a missing GUI library on a machine installed with `-nodesktop`. |
| `status` says `None` forever | No profile, or none chosen. `vhclient profile list`. |
| Connects, but no traffic | Check the log: `vhclient service log -n 100`. |

The service log is the journal's, so everything systemd knows is there too:
`journalctl -u VpnHoodClient -n 200`.

---

## How it fits together

For anyone changing this. The command line is a UI over the app's API, exactly as the Avalonia
window is, so it lives beside the Avalonia hosts and is split the same way: one shared project,
one small adapter per platform.

```
src/AppUi/Hosting/Cli/
├── Cli/            VpnHood.AppUi.Hosting.Cli          the commands, the daemon, the window launcher
│   ├── CliHost.cs                    builds the command tree and dispatches
│   ├── CliHeadParams.cs              what a head declares: its AppOptions, its UI, whether it takes keys
│   ├── CliPlatform.cs                what a platform declares: paths, the instance, how to be the daemon
│   ├── IAppCliPaths.cs               where this install keeps things
│   ├── IAppInstanceController.cs     the running instance: is it up, start, stop, log
│   ├── IAppDaemonHost.cs             the app built the way this OS hosts a headless one
│   ├── DaemonConnection.cs           the API over loopback, waiting for a service that is coming up
│   ├── DaemonInfo.cs                 the address the daemon publishes (storage/daemon.json)
│   ├── Commands/                     one type per command
│   └── Internal/                     printer, session, profile lookup
└── Cli.Linux/      VpnHood.AppUi.Hosting.Cli.Linux    systemd, /opt, XDG, root
    ├── LinuxCliHost.cs               a Linux head's entry point; catches the old launcher words
    ├── LinuxCliPaths.cs
    ├── LinuxInstanceController.cs    systemctl / journalctl, streams passed through
    └── LinuxDaemonHost.cs            VpnHoodAppLinux, root required
```

A head is then its `AppOptions` and one line naming its UI — see
[`Client.Linux.Web/App.cs`](../../src/Apps/Client/Client.Linux.Web/App.cs). Nothing in the shared
project reads a static or names a platform: every command is handed a `CliPlatform`, and the two
interfaces on it are all a second platform has to write. `VpnHood.AppLib.Linux.Common` is back to
the one thing it always was, `VpnHoodAppLinux`.

Two points that are easy to get wrong:

1. **The port is not a constant.** The local listener takes 4700 when it is free and whatever the
   OS gives when it is not (`VpnHoodAppWebHost.ResolvePort`). That is why the service publishes
   `daemon.json` and nothing dials a hard-coded port.
2. **The UI must not reach for `VpnHoodApp.Instance`.** It gets a `VpnHoodApi` — in process on
   Windows and Android, over loopback here — and the presentation layer already knows only
   `VhApp`. `AvaloniaDesktopHost.Run` has an overload for each.
3. **The seam is "the running instance", not "the service".** On Linux that is a systemd unit; on
   Windows it is the elevated tray app, which would publish `daemon.json` from its own startup and
   set `CliPlatform.CreateDaemonHost` to null, so there is no `daemon` command to type. A Store
   build is a third `IAppCliPaths` + `IAppInstanceController` pair, not a third design.
4. **Hints print `CommandName`, not the binary's name.** `vhclient` is on the `PATH`;
   `VpnHoodClient` is not. The launcher script says its own name in `VH_LAUNCHER_NAME`.

The installer is [`pub/lib/vh-installer/linux/install-client.sh`](../../pub/lib/vh-installer/linux/install-client.sh),
which is **not** the server's `install.sh` next to it. Each head picks its template in its
`_publish.ps1` (`-installTemplate`, `-logoAssetPath`).
