# Source layout — where an app's code lives

How `src/` is organised, what a platform app is made of, and what a fork copies. Written for
someone opening this repo for the first time.

## The four layers

Everything under `src/` is one of four things, and each layer only knows about the ones above it in
this table.

| Folder | What it is | Ships as |
| --- | --- | --- |
| `src/Core/` | the VPN engine: the tunnel, the adapters, the protocols, the server | NuGet |
| `src/AppLib/` | the app around the engine: `VpnHoodApp`, its options, its HTTP API and web host, and the per-platform glue (Android, iOS, Linux, Windows) | NuGet |
| `src/AppUi/` | the user interface: the app's state as a UI sees it, the look, and the hosts that mount a UI on a platform | NuGet |
| `src/Apps/` | the apps we ship, and the tools we run ourselves | the stores, our site |

Nothing in `Core`, `AppLib` or `AppUi` knows that VpnHood Client or VpnHood Connect exist. That is
the rule that makes a fork possible: a fork replaces `src/Apps/` and nothing else.

## How a folder is named

A folder under a layer is always a concept, never an OS, a store or a technology — those only ever
end a name. The folder path spells the project's name, and the `.csproj` inside keeps its full name.

1. **A family** — a base or `Abstractions` project and its variants — is one folder, and each
   member's folder starts with the family's name: `Core/Client/Devices/Devices.Android/` holds
   `VpnHood.Core.Client.Devices.Android.csproj`. The base repeats it: `Core/Client/Client/`.
2. **Peers that only share a kind** sit in a plain folder they don't repeat:
   `AppUi/Assets/Classic/`.
3. **A project that is a concept on its own** is just its folder: `Core/Common/`.

## `src/Core/` — the engine, one folder per concept

```text
src/Core/
├── Client/
│   ├── Client/
│   ├── Client.Abstractions/
│   ├── Devices/                    one IDevice per OS
│   │   ├── Devices.Abstractions/
│   │   ├── Devices.Android/
│   │   ├── Devices.Ios/
│   │   ├── Devices.Linux/
│   │   └── Devices.Win/
│   └── VpnServices/                the VPN service host and the manager that talks to it
│       ├── VpnServices.Abstractions/
│       ├── VpnServices.Host/
│       └── VpnServices.Manager/
├── Common/
├── Filtering/
│   ├── Filtering.Abstractions/
│   ├── Filtering.DomainFiltering/
│   └── Filtering.Sqlite/
├── IpLocations/
│   ├── IpLocations/
│   └── IpLocations.SqliteProvider/
├── Packets/
├── PacketTransports/
├── Proxies/
│   ├── Proxies.Management/
│   ├── Proxies.Management.Abstractions/
│   └── Proxies.Management.Sqlite/
├── Quic/
│   ├── Quic.Abstractions/
│   ├── Quic.Android/
│   ├── Quic.Ios/
│   └── Quic.MsQuic/
├── Server/
│   ├── Server/
│   └── Access/
│       ├── Access/
│       └── Access.FileAccessManager/
├── TcpStack/
│   ├── TcpStack/
│   └── TcpStack.Abstractions/
├── Toolkit/
├── Tunneling/
└── VpnAdapters/                    one IVpnAdapter per OS or driver
    ├── VpnAdapters.Abstractions/
    ├── VpnAdapters.AndroidTun/
    ├── VpnAdapters.IosTun/
    ├── VpnAdapters.LinuxTun/
    ├── VpnAdapters.WinDivert/
    └── VpnAdapters.WinTun/
```

## `src/Apps/` — one folder per product

```
src/Apps/
├── Client/
│   ├── Client/                     VpnHood.App.Client.csproj          the product
│   ├── Client.Android.Google/      the Play distribution
│   ├── Client.Android.Web/         the APK from our site
│   ├── Client.Ios.Apple/           the App Store distribution
│   ├── Client.Ios.Extension/       the Network Extension it bundles
│   ├── Client.Linux.Web/
│   ├── Client.Win.Web/
│   └── Client.Win.Web.Setup/       the Advanced Installer project (.aip), not a csproj
├── Connect/                        the same set, with Connect's own product project
├── Server/                         VpnHood.App.Server.Net.csproj + its Docker and Linux packaging
└── Tools/
    ├── AvaloniaUI.Dev/             the Avalonia UI on a PC, for a developer
    └── MacShim/                    an Xcode stub that installs iOS dev profiles on a Mac
```

**A head is `<Product>.<Platform>.<Channel>`.** The channel is who hands the app to the user:
`Google` is Play, `Apple` is the App Store, `Web` is our own site. Every head names its channel, so
a second one on the same platform is a sibling rather than a rename.

Two folders are not heads and do not follow that rule. `*.Ios.Extension` is the Network Extension
the iOS app bundles, which belongs to the platform rather than to a channel, so every iOS channel
of a product would reuse the same one. `*.Win.Web.Setup` holds the installer definition the
Windows publish script builds.

`Tools/` is what we run ourselves. Nothing in it ships, and nothing in it is built by CI.

## What a head is made of

A head is a thin shell — an activity, a view controller, a window — and usually only three files.

| File | What it does |
| --- | --- |
| `AppConfigs.cs` | derives from `AppConfigsBase` and implements `IRequiredAppConfigs` (both in `VpnHood.AppLib.App`), which is the list of settings a product must state: `AppId`, `UpdateInfoUrl`, `DefaultAccessKey`, `Ga4MeasurementId`, the legal URLs, the logo and consent names. Because the interface is a contract, a new head cannot forget one. Values can be overridden from a `.user` `appsettings.json` at build time. |
| `App.cs` (or `AppDelegate.cs`) | builds `AppOptions` from those configs, names the theme (`UiTheme`), names the two zips below, and starts `VpnHoodApp` on the platform's device |
| `_publish.ps1` | the one entry point that builds and packages this head; CI calls exactly this |

The head references its product project, the platform glue it needs from `AppLib`, and the UI
hosts it mounts from `AppUi`. A Linux head mounts one more host than the others: `Hosting/Cli`,
which is the command line, the headless daemon and the window launcher in one binary — see
[linux/](linux/README.md#how-it-fits-together).

## The product project

`Client/Client/` and `Connect/Connect/` hold what every distribution of that product agrees on:
the packages the product pins (today the IP-location database), the app framework and web host its
heads build on, and any product-wide constant — Connect's premium feature list lives there.

It is deliberately small. Everything that varies per platform stays in the head, and everything
that a fork would want different stays out of the libraries underneath.

## The two zips a head places beside itself

An app needs two blobs at runtime. Neither is embedded in an assembly, because Android keeps the
assembly store once per CPU architecture and anything inside a `.dll` would ship once per ABI. Both
arrive as **files placed by MSBuild targets**, and the head names each one in `AppOptions`.

| File | Comes from | Named by | Holds |
| --- | --- | --- | --- |
| `assets/ui.zip` | `VpnHood.AppUi.Assets.Classic` | `AppOptions.UiZipAssets` | images, country flags, fonts, content documents, the words of every language, and the per-theme branding the OS chrome draws with |
| `assets/web-root.zip` | `VpnHood.AppUi.Presentation.Classic.Avalonia.Browser` | `AppOptions.WebRootZipAsset` | the page the app's web host serves to a paired device |

Each of those projects owns a `build/*.targets` that places its file the way the platform reads
files — `AndroidAsset` on Android, `BundleResource` on Apple, copy-to-output elsewhere. In this
repo a head imports those targets directly; through a `PackageReference` the same placement arrives
via `buildTransitive/`.

## The browser page, end to end

The app's web host serves a page to a phone paired with a TV. That page is the Avalonia UI compiled
to WebAssembly - the same UI the head itself runs in process, drawn by a browser instead.

```
Presentation/Classic.Avalonia/            the UI itself: pages, controls, themes
Hosting/Avalonia/Avalonia.Browser/        mounts ANY Avalonia UI as a page: AvaloniaBrowserHost.RunAsync<TUi>
Presentation/Classic.Avalonia.Browser/    names which UI: a global.json, a csproj, a three-line Main
        │  _publish.ps1
        ▼
     bin/avalonia-browser.zip  ──build/*.targets──▶  <head>/assets/web-root.zip
```

The host library is the fourth Avalonia host beside `Avalonia.Desktop` and `Avalonia.Android`, and
it names no UI: it takes one as a type argument on the `IAvaloniaUi` contract, so the UI a build
does not name is not in the bundle. A second presentation gets its own page by adding its own
`.Browser` project of the same ten lines.

Nothing of the app runs in the page. It dials the app's HTTP API at the origin it was served from,
the browser's own cookie carrying the pairing, and fetches the UI's store by name from `/assets/` on
that same server.

**It must be built with the .NET 10 SDK**, which a `global.json` in the page project's folder pins,
plus the `wasm-tools` workload. SkiaSharp's WebAssembly libraries are built for that SDK's
Emscripten; the .NET 11 preview's cannot link them. Only this one project needs .NET 10 — the iOS
heads need .NET 11, and nothing else cares.

In CI the page is built **once**: the `build-browser` job in `publish_app.yml` runs on .NET 10,
publishes it, and uploads it as the `browser-page` artifact, which all four platform build jobs
download before they build. Locally, a head build that finds no zip warns and names the script to
run; the app then has a web host with no page to serve.

## What a fork copies

Copy one product folder — `Client/` or `Connect/` — and it becomes a third sibling. Inside it:
rename the folders and projects to your product, put your ids and URLs in each head's
`AppConfigs.cs`, and pin your own asset package if you are replacing the artwork. Everything else
is consumed from NuGet.

Nothing beneath `src/Apps/` may be edited for a fork, and nothing beneath it may depend on anything
inside it. If you find yourself wanting to change a library to brand your app, that is a bug in the
library's shape — see
[cicd/white-label-readiness.md](cicd/white-label-readiness.md) for what is still hardcoded.

## See also

- [topology.md](topology.md) — which component talks to which at runtime
- [publish-your-app/](publish-your-app/README.md) — standing up your own branded app in the stores
- [ios/](ios/README.md) — the iOS host and its Network Extension
- [`pub/RELEASE-STRATEGY.md`](../pub/RELEASE-STRATEGY.md) — versioning, and which packages are pinned
