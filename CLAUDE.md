# VpnHood — Claude instructions

The shared coding conventions and working agreements for this repo. They are the source of
truth — follow them, and when a new durable convention is agreed, update this file.

The design and code rules agreed while building are in
[`docs/implementation-rules.md`](docs/implementation-rules.md): read them before implementing.

## Repo layout
- Top-level directories are lowercase: `src/`, `tests/`, `tools/`, `samples/`, `docs/`, `pub/`. Shared
  script folders are lowercase too (`pub/lib`, `pub/lib/utils`, `pub/lib/vh-installer`), and so are
  `src/`'s two, `src/libs` and `src/apps`; layer, product and project folders keep their own casing
  (`src/libs/AppLogic`, `src/apps/Client`). Module repos (e.g. VpnHood.Net.Proxies) follow the same
  convention (`pub/PubVersion.json`).
- `src/` holds only what ships: the library layers in `src/libs/` and the apps in `src/apps/`, one
  folder per product (`Client/`, `Connect/`, `Server/`). What we run ourselves sits beside it: `tools/`
  (the Avalonia dev head, MacShim, the store listing and its screenshots) and `tests/`. A head is
  `<Product>/<Product>.<Platform>.<Channel>` (`Client/Client.Android.Google`, `Connect/Connect.Ios.Apple`);
  the product's own project, what every distribution of it shares, is `<Product>/<Product>`. A fork
  copies one product folder as its template. The browser page every head serves lives with the UI it
  compiles (`src/libs/AppUi/VpnHood.AppUi.Avalonia.Classic.Browser`), whose `_publish.ps1` writes it
  into the UI's own package (`VpnHood.AppUi.Avalonia.Classic/buildTransitive/web-root.zip`),
  placed beside each head as `assets/web-root.zip` by that package's targets; `publish_nugets.yml` builds
  it before the pack, and CI builds it once (`build-browser` in `publish_app.yml`) and hands it to every
  platform build.
- Shared PowerShell scripts under `pub/` are PascalCase Verb-Noun with approved verbs
  (`Invoke-VersionBump.ps1`, `pub/lib/Publish-NugetPackages.ps1`). Dot-sourced libraries/config
  keep noun names (`Common.ps1`, `AppPublishConfig.ps1`); per-app entry scripts keep their
  folder-scoped names (`pub/Client/Publish.ps1`, `src/apps/*/*/_publish.ps1`).

## Documentation

- User and server-operator documentation lives on the website, in the separate repository `..\VpnHood.www`
  (`vpnhood-client/docs/`, `vpnhood-server/docs/`, `vpnhood-manager/docs/`, `self-hosted-vpn/docs/`), in all 13
  languages. A change to what a user or an operator sees or does updates it in the same work.
- `docs/` in this repo is developer documentation, published on the developer site; development and internal docs
  stay here.
- `download/` is the GitHub copy of the CLIENT and CONNECT download pages, for readers who cannot reach the website;
  it changes together with its website page. The pictures GitHub shows for this repo, in the README and
  `download/`, are in `docs/images/`, never loaded from the website.
- The wiki (`..\VpnHood.wiki`) is retired: its pages only point to their new places until it is switched off.
  Never add or move content there.

## Naming

- "Proxy" is only someone else's real proxy: a SOCKS or HTTP proxy a user adds, or a reverse proxy. What the engine
  does itself is forwarding: TCP forwarding, `StreamForwardingChannel`, `UdpForwarder`. A relay is a service, which
  forwards; "relay" stays free for one.
- A name says what its thing handles. The channel joins any two streams, so it is `StreamForwardingChannel`; the
  request asks the server for a channel that forwards TCP, the only thing it opens, so it is
  `TcpForwardingChannelRequest`, beside `TcpPacketChannelRequest`.
- Stream forwarding and packet forwarding stay apart: `StreamForwardingBufferSize` sizes the channels' pumps, and
  `PacketForwardingQueueCapacity` is the queue before each UDP forwarder. A "Tcp" there would sit beside
  `UdpForwardingBufferSize` and read as packets.
- "Cloak" lives in the translations and on the page that explains Cloak Mode. All other code says TCP forwarding, and
  keeps three things apart: the user's wish (`UseTcpForwarding`), what the switch shows
  (`VhApp.IsTcpForwardingSwitchOn`) and what really runs, per IP version (`UseTcpForwardingIpV4`, `UseTcpForwardingIpV6`).

## CI/CD & publishing

- All app **builds** (`.ipa`/AAB/MSI/Linux) and **Fastlane publishing** (Google Play, TestFlight/App Store,
  store listings) run on **GitHub Actions — never from a developer machine**. Don't build release packages
  or run Fastlane locally; the runners hold the signing keys, toolchains, and store credentials. A local
  build is only ever for a quick smoke test, not for distribution.
- The `fastlane/` **submodule is CI-only**: never init/fetch it on a developer machine (no
  `git submodule update --init`). Keep it registered but uninitialized locally; only the GitHub runners
  check it out.

## Area rules

Loaded automatically by file path. When a task touches an area before any of its files has been
opened (a question, a plan), read the rule file first. A new convention for one area goes into its
rule file.

- [`.claude/rules/csharp.md`](.claude/rules/csharp.md) — C# language and `await`/`ConfigureAwait` conventions.
- [`.claude/rules/ui.md`](.claude/rules/ui.md) — the Avalonia UI and its Assets submodule.
- [`.claude/rules/quic.md`](.claude/rules/quic.md) — our QUIC is a transport treated like TCP, not HTTP3.
- [`.claude/rules/ios.md`](.claude/rules/ios.md) — iOS heads and extensions: where the code lives, `docs/ios/`,
  the .NET 11 build, the default app, App Store-only updates.
- [`.claude/rules/ci-cd.md`](.claude/rules/ci-cd.md) — brand repos, the publish dispatchers, the App Store
  listing workflow.
