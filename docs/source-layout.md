# Source layout — where an app's code lives

How `src/` is organised, what a platform app is made of, and what a fork copies. Written for
someone opening this repo for the first time.

## The five layers

Everything under `src/` is one of five things, and each layer only knows about the ones above it in
this table.

| Folder | What it is | Ships as |
| --- | --- | --- |
| `src/Net/` | networking with no VPN in it: packets, a user-space TCP stack, QUIC, the TUN and WinDivert adapters, IP locations, and the toolkit they share | NuGet |
| `src/Core/` | the VPN engine: the tunnel, the client, the server, split tunneling and proxies | NuGet |
| `src/AppLib/` | the app around the engine: `VpnHoodApp`, its options, its HTTP API and web host, the app on each platform (`App.Android`, `App.Ios`, `App.Linux`, `App.Windows`), and the store and ad providers | NuGet |
| `src/AppUi/` | the user interface: the app's state as a UI sees it, the look, and the hosts that mount a UI on a platform | NuGet |
| `src/Apps/` | the apps we ship, and the tools we run ourselves | the stores, our site |

Nothing in `Net`, `Core`, `AppLib` or `AppUi` knows that VpnHood Client or VpnHood Connect exist. That
is the rule that makes a fork possible: a fork replaces `src/Apps/` and nothing else. `Net` goes one
step further and knows nothing of a VPN, so any .NET app can use it.

## How a folder is named

**In `Net`, `Core`, `AppLib` and `AppUi`, every project sits directly under its layer, in a folder
named with its full project id**: `VpnHood.Core.Client.Devices.Android.csproj` lives in
`src/Core/VpnHood.Core.Client.Devices.Android/`. There are no grouping folders, as in dotnet/runtime's
`src/libraries/`: the ids sort a family together on their own (`VpnHood.Net.Quic.*`,
`VpnHood.AppLib.Stores.*`), and the id alone says where a project is. A layer's folder is the second
word of its ids: `src/Net/` holds `VpnHood.Net.*`, `src/Core/` holds `VpnHood.Core.*`.

The solution file keeps the grouping on screen: `VpnHood.slnx` has a solution folder per family
(`Devices`, `Quic`, `Stores`, `Hosting`, …), so the IDE shows a tree while the disk stays flat.

A project's namespace is its name. Inside a namespace that ends in `.Android`, C# resolves `Android`
to that namespace before the platform's, so the platform's own types are written
`global::Android.Net.Uri` where they are not imported with a `using`.

## `src/Net/` — networking, no VPN

```text
src/Net/
├── VpnHood.Net.IpLocations/                    the country of an IP address
├── VpnHood.Net.IpLocations.Providers.SqliteProvider/
├── VpnHood.Net.PacketTransports/
├── VpnHood.Net.Packets/                        IPv4/IPv6, TCP, UDP and ICMP packets
├── VpnHood.Net.Quic.Abstractions/              QUIC on every platform, MsQuic or native
├── VpnHood.Net.Quic.Android/
├── VpnHood.Net.Quic.Ios/
├── VpnHood.Net.Quic.MsQuic/
├── VpnHood.Net.TcpStack/                       a user-space TCP stack over raw packets
├── VpnHood.Net.TcpStack.Abstractions/
├── VpnHood.Net.Toolkit/                        the logging, IP ranges and helpers the rest share
├── VpnHood.Net.VpnAdapters.Abstractions/       one IVpnAdapter per OS or driver
├── VpnHood.Net.VpnAdapters.AndroidTun/
├── VpnHood.Net.VpnAdapters.IosTun/
├── VpnHood.Net.VpnAdapters.LinuxTun/
├── VpnHood.Net.VpnAdapters.WinDivert/
└── VpnHood.Net.VpnAdapters.WinTun/
```

## `src/Core/` — the engine

```text
src/Core/
├── VpnHood.Core.Client/
├── VpnHood.Core.Client.Abstractions/
├── VpnHood.Core.Client.Devices.Abstractions/       one IDevice per OS
├── VpnHood.Core.Client.Devices.Android/
├── VpnHood.Core.Client.Devices.Ios/
├── VpnHood.Core.Client.Devices.Linux/
├── VpnHood.Core.Client.Devices.Windows/
├── VpnHood.Core.Client.VpnServices.Abstractions/   the VPN service host and the manager that talks to it
├── VpnHood.Core.Client.VpnServices.Host/
├── VpnHood.Core.Client.VpnServices.Manager/
├── VpnHood.Core.Common/
├── VpnHood.Core.Filtering.Abstractions/
├── VpnHood.Core.Filtering.DomainFiltering/
├── VpnHood.Core.Filtering.Sqlite/
├── VpnHood.Core.Proxies.Management/
├── VpnHood.Core.Proxies.Management.Abstractions/
├── VpnHood.Core.Proxies.Management.Sqlite/
├── VpnHood.Core.Server/
├── VpnHood.Core.Server.Access/
├── VpnHood.Core.Server.Access.Managers.FileAccessManagers/
└── VpnHood.Core.Tunneling/
```

## `src/AppLib/` — the app

```text
src/AppLib/
├── VpnHood.AppLib.Abstractions/                the contracts the providers implement
├── VpnHood.AppLib.Ads.AdMob.Android/
├── VpnHood.AppLib.Api/                         the app's API: its interfaces and DTOs
├── VpnHood.AppLib.Api.HttpClients/             the API over HTTP, for a UI outside the app's process
├── VpnHood.AppLib.Api.WebHost/                 the web host that serves it
├── VpnHood.AppLib.App/                         VpnHoodApp and AppOptions
├── VpnHood.AppLib.App.Android/                 the app on each platform: VpnHoodAndroidApp, VpnHoodIosApp, …
├── VpnHood.AppLib.App.Ios/
├── VpnHood.AppLib.App.Linux/
├── VpnHood.AppLib.App.Windows/
├── VpnHood.AppLib.Portal/                      accounts and purchases through the VpnHood Portal API
├── VpnHood.AppLib.Stores.AppStore/             App Store billing and Sign in with Apple
├── VpnHood.AppLib.Stores.AppStore.Core/        App Store updates and reviews
├── VpnHood.AppLib.Stores.GooglePlay/           Play Billing and Google sign-in
└── VpnHood.AppLib.Stores.GooglePlay.Core/      Play in-app updates and reviews
```

The store packages take their names from `StoreIds`, so a Microsoft Store package would be
`VpnHood.AppLib.Stores.Microsoft`. A project's namespace is its id: `VpnHood.AppLib.App.Android`,
`VpnHood.AppLib.Stores.GooglePlay`, `VpnHood.AppLib.Ads.AdMob.Android`.

## `src/Apps/` — one folder per product

```text
src/Apps/
├── Client/
│   ├── Client/                     VpnHood.App.Client.csproj          the product
│   ├── Client.Android.Google/      the Play distribution
│   ├── Client.Android.Web/         the APK from our site
│   ├── Client.Ios.Apple/           the App Store distribution
│   ├── Client.Ios.Extension/       the Network Extension it bundles
│   ├── Client.Linux.Web/
│   ├── Client.Windows.Web/
│   └── Client.Windows.Web.Setup/       the Advanced Installer project (.aip), not a csproj
├── Connect/                        the same set, with Connect's own product project
├── Server/                         VpnHood.App.Server.Net.csproj + its Docker and Linux packaging
└── Tools/
    ├── AvaloniaUI.Dev/             the Avalonia UI on a PC, for a developer
    ├── MacShim/                    an Xcode stub that installs iOS dev profiles on a Mac
    └── StoreScreenshots/           renders the store screenshots
```

`src/Apps/` is not a library layer, so the full-id rule above does not apply to it: a product is a
folder, and a head's folder is `<Product>.<Platform>.<Channel>` without the `VpnHood.App.` prefix.

**A head is `<Product>.<Platform>.<Channel>`.** The channel is who hands the app to the user:
`Google` is Play, `Apple` is the App Store, `Web` is our own site. Every head names its channel, so
a second one on the same platform is a sibling rather than a rename.

Two folders are not heads and do not follow that rule. `*.Ios.Extension` is the Network Extension
the iOS app bundles, which belongs to the platform rather than to a channel, so every iOS channel
of a product would reuse the same one. `*.Windows.Web.Setup` holds the installer definition the
Windows publish script builds.

`Tools/` is what we run ourselves. Nothing in it ships, and nothing in it is built by CI.

## What a head is made of

A head is a thin shell — an activity, a view controller, a window — and usually only two files. Its
ids and names are not among them: they come from the app's identity (below).

| File | What it does |
| --- | --- |
| `App.cs` (or `AppDelegate.cs`) | states the head's init params (`AppInitParams`): the app id, the storage folder, and a factory that builds `AppOptions` — it loads the product's settings (`ClientAppConfigs.Load`, or `ConnectAppConfigs.Load`, which adds the head's built-in key), then the product's options (`ClientAppOptions.Create`), which name the logo, the theme and the two zips below, and its channel's lines on top: its update feed and updater, its store's review and billing. On Android and iOS the class derives from its UI's host (`AndroidAvaloniaApplication<TUi>`, `IosAvaloniaAppDelegate<TUi>`), which starts the app and then the UI; on a desktop `Main` hands the params and its UI to the platform's host (`WindowsCliHost.Run`, `LinuxCliHost.Run`), whose service starts the app. Either way the platform builds its own device and starts `VpnHoodApp` |
| `_publish.ps1` | the one entry point that builds and packages this head; CI calls exactly this |

The head references its product project, the platform glue it needs from `AppLib`, and the UI
hosts it mounts from `AppUi`. A desktop head mounts one more host than a mobile one: its platform's
`VpnHood.AppUi.Hosting.Cli.Linux` or `.Cli.Windows`, over the shared `VpnHood.AppUi.Hosting.Cli`,
which is the service that holds the app, the window that shows it and the command line in one
binary — see [linux/](linux/README.md#how-it-fits-together).

## The product project

`Client/Client/` and `Connect/Connect/` hold what every distribution of that product agrees on:
the options every head shares (`ClientAppOptions`, `ConnectAppOptions`), the product's settings —
what its private appsettings can say (`ClientAppConfigs`, `ConnectAppConfigs`, over `AppConfigs` in
`VpnHood.AppLib.App`), with the files from `.user/<product>` embedded here once for every head —
the packages the product pins (today the IP-location database), the app framework and web host its
heads build on, and any product-wide constant — Connect's premium feature list lives there. A
setting has no value in code: one the appsettings do not name stays null, and what needs it is off
or fails where it is used.

It is deliberately small. Everything that varies per platform stays in the head, and everything
that a fork would want different stays out of the libraries underneath.

## The app's identity

Each product folder has a `Directory.Build.props` that states the app once, for every head in it:

| Property | Client | What it is |
| --- | --- | --- |
| `VhAppIdBase` | `com.vpnhood.client` | every id of the app starts with it |
| `VhAppName` | `VpnHood! CLIENT` | the name the app shows |
| `VhAppPackageTitle` | `VpnHoodClient` | the name without spaces: the start of every release file's name, the update feeds' included (`AppConstants.PackageTitle`); the desktop executable, its service or unit, their folders. Taken from the name's letters and digits when not stated; an app that has shipped states it, since installs keep these names |
| `Company` | `OmegaHood LLC` | the app's maker, as the UI (`AppConstants.CompanyName`) and the executable's details name it, with a copyright made from it where none is stated. The repo's own `Directory.Build.props` states it for every project, so the products do not |

A build target in `VpnHood.AppLib.App` (`buildTransitive/VpnHood.AppLib.App.targets`, the file a
package reference of it imports) makes the rest, so a head names no id and no name:

- **the app id** (`ApplicationId`): the base, the platform, then `.web` for a build our website hands
  out (the head states `VhAppIdChannel`), then `.debug` in Debug, except on iOS, where every bundle id
  needs a provisioning profile of its own. Connect's Windows installer is
  `com.vpnhood.connect.windows.web`;
- **the name a build shows**: the app's name, with `(DEBUG)` after it in Debug;
- **on iOS**, the bundle's display name, the network extension's id (`<base>.ios.networkextension`)
  and the App Group (`group.<base>.ios`), which the build adds to both targets' entitlements;
- **`AppConstants`**, a class the build writes for the code: `AppId`, `AppName`, `CompanyName`,
  `PackageTitle` and `IsDebugMode`, and
  on iOS `AppGroupId` and `ProviderBundleId`. At run time Android and iOS read the id their package or
  bundle has; the desktops read `AppConstants.AppId`. The product library gets one too, without the
  ids, which are each head's own: its options builder reads the rest there, so a head passes none.

What a head states itself is kept. A shipped id never changes - the stores key their listings on it,
and on Windows and Linux it is part of every install's client id - so a head whose shipped id the
rule does not give states it: Client's Windows and Linux heads name no channel, and Connect's Play
head keeps its Play id in Debug, the only package its Firebase configuration knows.

## The two zips a head places beside itself

An app needs two blobs at runtime. Neither is embedded in an assembly, because Android keeps the
assembly store once per CPU architecture and anything inside a `.dll` would ship once per ABI. Both
arrive as **files placed by MSBuild targets**, and the head names each one in `AppOptions`.

| File | Comes from | Named by | Holds |
| --- | --- | --- | --- |
| `assets/ui.zip` | `VpnHood.AppUi.Assets.Classic` | `AppOptions.UiZipAssets` | images, country flags, fonts, content documents, the words of every language, and the per-theme branding the OS chrome draws with |
| `assets/web-root.zip` | `VpnHood.AppUi.Presentation.Classic.Avalonia.Browser` | `AppOptions.WebRootZipAsset` | the page the app's web host serves to a paired device |

Each of those projects owns a targets file that places its file the way the platform reads files —
`AndroidAsset` on Android, `BundleResource` on Apple, copy-to-output elsewhere — and in this repo a
head imports both directly. The store's is `buildTransitive/*.targets`, which a `PackageReference`
brings at any depth; the page's is `build/*.targets`, which reaches no package consumer, since the
page's project is not packed.

## The browser page, end to end

The app's web host serves a page to a phone paired with a TV. That page is the Avalonia UI compiled
to WebAssembly - the same UI the head itself runs in process, drawn by a browser instead.

```text
VpnHood.AppUi.Presentation.Classic.Avalonia/          the UI itself: pages, controls, themes
VpnHood.AppUi.Hosting.Avalonia.Browser/               mounts ANY Avalonia UI as a page: AvaloniaBrowserHost.RunAsync<TUi>
VpnHood.AppUi.Presentation.Classic.Avalonia.Browser/  names which UI: a global.json, a csproj, a three-line Main
        │  _publish.ps1
        ▼
     bin/avalonia-browser.zip  ──build/*.targets──▶  <head>/assets/web-root.zip
```

The host library is an Avalonia host like `Avalonia.Desktop`, `Avalonia.Android` and
`Avalonia.Ios`, and like them it runs the one start `VpnHood.AppUi.Hosting.Avalonia` writes
(`AvaloniaUiHosting`) and names no UI: it takes one as a type argument on the `IAvaloniaUi` contract, so the UI a build
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
rename the folders and projects to your product, put your app's id base and name in the product's
`Directory.Build.props` (see [the app's identity](#the-apps-identity)) and each head's update feed in
its `App.cs`, your logo and consent summary in the product's options builder, your settings - links, analytics, portal, ad ids - in your own private
appsettings, and pin your own asset package if you are replacing the artwork. Everything else is
consumed from NuGet.

Nothing beneath `src/Apps/` may be edited for a fork, and nothing beneath it may depend on anything
inside it. If you find yourself wanting to change a library to brand your app, that is a bug in the
library's shape — see
[cicd/white-label-readiness.md](cicd/white-label-readiness.md) for what is still hardcoded.

## See also

- [topology.md](topology.md) — which component talks to which at runtime
- [publish-your-app/](publish-your-app/README.md) — standing up your own branded app in the stores
- [ios/](ios/README.md) — the iOS host and its Network Extension
- [`pub/RELEASE-STRATEGY.md`](../pub/RELEASE-STRATEGY.md) — versioning, and which packages are pinned
