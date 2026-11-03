# Client architecture — the app and its VPN service

Every VpnHood client is two halves that talk over a channel: **the app**, and **the VPN service**,
which holds the tunnel. On every platform they run in separate processes, except in a developer's run.
Knowing which half a type or a project belongs to answers most "where does this go" questions.

## The two halves

| | The app | The VPN service |
| --- | --- | --- |
| What it does | settings, profiles, accounts, always-on, the API the UI calls | the tunnel: the session, the engine, the adapter |
| Its end of the channel | `VpnServiceManager` | `VpnServiceHost` |
| Its part of the OS | the device (`IDevice`): it starts the VPN service, opens the channel and says what the OS can do | the platform's VPN service (`<OS>VpnService`): the component the OS runs a VPN in, which makes the adapter and the channel's listener |
| Its projects | `AppLib`, `Core.Client.VpnServices.Manager`, `Core.Client.Devices.*` | `Core.Client.VpnServices.Host.*`, `Core.Client`, `Core.Tunneling` |

The two never share memory, even in one process. The manager sends requests through the device's
message client (`IMessageClient`), the VPN service answers through its listener (`IMessageListener`),
and both read the options and status files in the VPN service's folder. Each platform brings its own
transport for that channel; the desktop uses TCP on loopback.

## Where each half runs

| Platform | The app runs in | The VPN service runs in | What starts the VPN service | The VPN service's type |
| --- | --- | --- | --- | --- |
| Android | the app's process | a process of its own (Release builds) | Android, from the app's manifest | `AndroidVpnService` |
| iOS, tvOS | the app | the Network Extension | iOS, from the extension's `Info.plist` | `IosVpnService` |
| Windows, Microsoft Store | the window's process | the VPN plug-in, which Windows activates | `WindowsVpnPluginHost.Run` | `WindowsVpnPluginService` |
| Windows (WinTun) | the daemon, a Windows service; the window is its client | the VPN host, an executable the daemon starts at each connect | `WindowsVpnHost.Run` | `WindowsVpnService` |
| Linux | the daemon, a systemd unit; the window is its client | the VPN host, as on Windows | `LinuxVpnHost.Run` | `LinuxVpnService` |

The VPN service runs in the app's process in an Android Debug build, in a desktop head's `dev` run, in
`AvaloniaUI.Dev`, in a MAUI app on Windows and in the tests, through the same channel. A breakpoint in
the tunnel is then reached from one debugger run.

## The projects

```text
AppLib.App.<OS>
    ├──→ AppLib.App ──→ Core.Client.VpnServices.Manager
    └──→ Core.Client.Devices.<OS>
             └──→ Core.Client.Devices.Abstractions ──→ Core.Client.VpnServices.Abstractions

Core.Client.VpnServices.Host.<OS>
    ├──→ Core.Client.Devices.<OS>
    ├──→ Core.Client.VpnServices.Host ──→ Core.Client ──→ Core.Tunneling
    └──→ Net.VpnAdapters.<OS>
```

| OS | The app's side: `Core.Client.Devices.<OS>` | The VPN service's side: `Core.Client.VpnServices.Host.<OS>` |
| --- | --- | --- |
| `Android` | `AndroidDevice`, `AndroidMessageClient` | `AndroidVpnService`, `AndroidMessageListener` |
| `Ios` | `IosDevice`, `IosMessageClient` | `IosVpnService`, `IosMessageListener` |
| `WindowsTun` | `WindowsDevice`, `WindowsVpnHostProcess` | `WindowsVpnService`, `WindowsVpnHost` |
| `WindowsVpnPlugin` | `WindowsVpnPluginDevice`, `WindowsVpnPluginAppServiceClient` | `WindowsVpnPluginService`, `WindowsVpnPluginHost` |
| `Linux` | `LinuxDevice`, `LinuxVpnHostProcess` | `LinuxVpnService`, `LinuxVpnHost` |

The rules the references keep:

1. **Nothing on the app's side references the VPN service's side.** AppLib, AppUi, the stores, the
   ads and every `Core.Client.Devices.*` project compile without the engine (`Core.Client`,
   `Core.Tunneling`) and without `Core.Client.VpnServices.Host.*`.
2. **`Core.Client.Devices.<OS>` is the app's half of the OS, plus what both halves share**: the VPN
   service's folders and names, the kill switch, the system logger, Android's message transport. The
   VPN service's side references it for those; nothing in it runs a tunnel.
3. **`Core.Client.VpnServices.Host.<OS>` is the OS's VPN service and its entry point**, and never
   references AppLib.
4. **The device stays in Core.** It is how an app drives the VPN service without AppLib: a
   developer's own app references `Core.Client.Devices.<OS>` and `Core.Client.VpnServices.Manager`, and
   none of AppLib.
5. **A head composes the two.** An app head references `AppLib.App.<OS>`; a VPN service head (an iOS
   extension, a VPN host, a VPN plug-in) references `Core.Client.VpnServices.Host.<OS>` alone. A head
   whose package carries both halves (Android's APK), or that runs the VPN service in its own process
   (a desktop head, for `dev`), references both and hands the device its VPN service.

## Words

- **The app**: `VpnHoodApp` and what it runs. On the desktop it runs in **the daemon**, which the OS
  runs as a service - a Windows service, a systemd unit - so the desktop's commands that register and
  read it say "service" (`service install`, `service log`). "The VPN service" always means the tunnel's
  side.
- **The device**: the app's bridge to the OS (`IDevice`).
- **The VPN service**: the tunnel's side, on every platform: the OS's VPN component - Android's
  `VpnService`, iOS's packet tunnel provider, Windows' VPN plug-in, or on the desktop our own - around
  a `VpnServiceHost` and the engine.
- **The manager and the host**: the channel's two ends, `VpnServiceManager` in the app and
  `VpnServiceHost` in the VPN service.
- **The VPN host**: the executable the desktop daemon starts for the VPN service, and its entry point
  (`WindowsVpnHost`, `LinuxVpnHost`). A Store package's VPN plug-in has the same role
  (`WindowsVpnPluginHost`). On Android and iOS the OS makes that process.

## See also

- [source-layout.md](source-layout.md) — where each project lives, and what a head is made of
- [linux/README.md](linux/README.md#how-it-fits-together) — the daemon, the window and the command line on Linux
- [ios/architecture-and-ipc.md](ios/architecture-and-ipc.md) — the app and its Network Extension on iOS
