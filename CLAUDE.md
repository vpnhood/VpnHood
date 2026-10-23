# VpnHood — Claude instructions

The shared coding conventions and working agreements for this repo. They are the source of
truth — follow them, and when a new durable convention is agreed, update this file.

The design and code rules agreed while building are in
[`docs/implementation-rules.md`](docs/implementation-rules.md): read them before implementing.

## Repo layout
- Top-level directories are lowercase: `src/`, `tests/`, `samples/`, `docs/`, `pub/`. Shared
  script folders are lowercase too (`pub/lib`, `pub/lib/utils`, `pub/lib/vh-installer`); project
  folders keep their own casing (`src/Apps`, …). Module repos (e.g. VpnHood.Net.Proxies) follow
  the same convention (`pub/PubVersion.json`).
- `src/Apps/` holds only what ships, one folder per product (`Client/`, `Connect/`, `Server/`) plus
  `Tools/` for what we run ourselves (the Avalonia dev head, MacShim). A head is
  `<Product>/<Product>.<Platform>.<Channel>` (`Client/Client.Android.Google`, `Connect/Connect.Ios.Apple`);
  the product's own project, what every distribution of it shares, is `<Product>/<Product>`. A fork
  copies one product folder as its template. The browser page every head serves lives with the UI it
  compiles (`src/AppUi/VpnHood.AppUi.Presentation.Classic.Avalonia.Browser`), whose `_publish.ps1` writes it
  into the UI's own package (`VpnHood.AppUi.Presentation.Classic.Avalonia/buildTransitive/web-root.zip`),
  placed beside each head as `assets/web-root.zip` by that package's targets; `publish_nugets.yml` builds
  it before the pack, and CI builds it once (`build-browser` in `publish_app.yml`) and hands it to every
  platform build.
- Shared PowerShell scripts under `pub/` are PascalCase Verb-Noun with approved verbs
  (`Invoke-VersionBump.ps1`, `pub/lib/Publish-NugetPackages.ps1`). Dot-sourced libraries/config
  keep noun names (`Common.ps1`, `AppPublishConfig.ps1`); per-app entry scripts keep their
  folder-scoped names (`pub/Client/Publish.ps1`, `src/Apps/*/*/_publish.ps1`).

## Documentation

- The wiki repo is a separate repository located at `..\VpnHood.wiki` relative to this repo (i.e.
  `$(SolutionDir)/../VpnHood.wiki`). It holds end-user documentation, not development/internal docs. Update
  it only when I request; keep developer docs inside this repo.

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
- [`.claude/rules/ui.md`](.claude/rules/ui.md) — the Avalonia UI, its Assets submodule and the SPA sample repo.
- [`.claude/rules/quic.md`](.claude/rules/quic.md) — our QUIC is a transport treated like TCP, not HTTP3.
- [`.claude/rules/ios.md`](.claude/rules/ios.md) — iOS heads and extensions: where the code lives, `docs/ios/`,
  the .NET 11 build, the default app, App Store-only updates.
- [`.claude/rules/ci-cd.md`](.claude/rules/ci-cd.md) — brand repos, the publish dispatchers, the App Store
  listing workflow.
