# Release & Versioning Strategy

Developer-facing notes on how VpnHood is versioned and released, why it is set up this way, and
what we deliberately deferred. This is the source of truth for the release-pipeline direction; keep
it updated when the model changes.

## The challenge

The repo is one large solution (~70 projects, ~60 of them NuGet libraries) plus the apps
(Client, Connect, Server). Historically:

1. **One global version** ([pub/PubVersion.json](PubVersion.json)) is applied to *every* project
   at pack time (`dotnet pack -p:Version=…` in
   [pub/lib/Publish-NugetPackages.ps1](lib/Publish-NugetPackages.ps1)). Every release re-versions and re-pushes
   all ~60 NuGets even when they did not change — pure churn.
2. **The bump ran on a developer's machine** (inside `Publish.ps1 -bump`), so two people releasing
   could collide on the version file.
3. **`main` was updated with `push origin develop:main --force`**, which rewrites `main`'s
   history and breaks every fork/clone that tracks it.
4. **NuGet-in-development confusion**: "if I change a project, do I have to wait for a NuGet build?"

## How NuGet actually works here (resolves the confusion)

You do **not** publish/consume local NuGets during development. The standard SDK model:

- **Same repo → `ProjectReference`.** Develop against local source; no waiting on NuGet builds.
- **Pack converts it automatically.** `dotnet pack` on a packable project rewrites each
  `<ProjectReference>` into a NuGet `<dependency id=… version=…>` in the `.nupkg` (using the
  referenced project's `PackageId` + `Version`). That is why nuget.org shows dependencies even
  though the source uses project references — this is correct, not a bug.
- **Third-party / cross-repo → `PackageReference`.**
- Gotcha: `<ProjectReference … PrivateAssets="all">` suppresses the dependency *and* does not bundle
  the DLL by default — only use it for genuinely build-only references.

Rule of thumb: **same repo = ProjectReference, third-party = PackageReference, release output =
`.nupkg` with NuGet dependencies.**

## Target model (current direction, monorepo — no submodules)

1. **CI owns the bump.** A dedicated `bump` action ([.github/workflows/bump.yml](../.github/workflows/bump.yml)
   → [pub/Invoke-VersionBump.ps1](Invoke-VersionBump.ps1)) increments `PubVersion.json` (+ `Directory.Build.props`), commits,
   and pushes to `develop` (a stable bump additionally fast-forwards `main`). The CHANGELOG is
   hand-maintained (leading `# Latest` section) and never rewritten by CI. Local machines never bump →
   no cross-developer conflicts. It can optionally chain straight into the client publish and/or NuGet publish. **(Done.)**
2. **`develop` is the prerelease line; `main` is the stable/release line.** [pub/Invoke-VersionBump.ps1](Invoke-VersionBump.ps1)
   always pushes `HEAD:develop`; on a **stable** bump it ALSO fast-forwards `HEAD:main` **without
   `--force`** (a **prerelease** bump leaves `main` untouched — prereleases ship to TestFlight / Play
   alpha, not the App Store / Play production). `main` only ever fast-forwards from `develop`, so it is
   a clean fast-forward; a rejection signals a real divergence to reconcile by hand rather than
   overwrite. Protects forkers. **(Done.)**
3. **NuGet is always a stable Release version.** Packing is `-c Release`; the version does not get a
   `-prerelease` suffix from the app flag ([pub/lib/Publish-NugetPackages.ps1](lib/Publish-NugetPackages.ps1)).
   One clean library version line, decoupled from app prereleases. (The `smoke` input is the only
   exception — see below.) **(Done.)**
4. **Release tags are immutable, and pinned to an exact commit.**
   [pub/lib/Publish-GithubRelease.ps1](lib/Publish-GithubRelease.ps1) replaces the *release object* on a
   re-publish but never the *tag* — no `--cleanup-tag` — and creates a new tag with an explicit
   `--target <sha>` supplied by [.github/workflows/publish_app.yml](../.github/workflows/publish_app.yml).
   Deleting a tag to recreate it is a history rewrite by another name: `gh release create` re-cuts it at
   the target repo's **default-branch tip**, so the tag silently walks forward on every re-publish, stops
   identifying the released code (once Connect's default branch became `develop`, its tags landed on
   commits unreachable from `main`), and leaves every clone that already fetched it unable to push tags
   (`! [rejected] … already exists`) until it re-syncs with `git fetch --tags --force`. `target_commitish`
   is *unused when the tag already exists*, so the pin applies at creation and the tag is frozen after.
   Same principle as `main` never being force-pushed: published refs are not rewritten. **(Done.)**
   - A thin caller repo carries no source, so its tag pins its *own* commit (the fastlane/config used) and
     the built monorepo commit is recorded in the release note instead. **(Done.)**
5. **One shared version for everything in `src/`.** All projects (apps + libraries) carry the
   single `PubVersion.json` version and bump together on a release — the standard, lowest-maintenance
   model. We accept that unchanged libraries get a new version number on a release; chasing
   per-library churn is not worth the machinery. Genuine per-component independence is deferred to a
   future `modules/` folder (see Deferred options). This supersedes an earlier idea of a separate
   library-publish trigger.

## Deferred options (recorded for the future)

These were considered and intentionally **not** done now. Revisit if the pain grows.

- **Central Package Management (CPM).** Add a root `Directory.Packages.props`
  (`ManagePackageVersionsCentrally=true`) for *third-party* versions, and a root
  `Directory.Build.props` for the *produced* package version/metadata. Strongly recommended as the
  next structural cleanup — one place for dependency versions across ~70 projects — but it is a
  separate mechanical migration (strip per-csproj versions) with build-breakage risk, so it is kept
  out of the pipeline refactor. Neither file exists today.
- **Git submodules for independent libraries.** The right tool *only* if a library has a genuinely
  independent life (outside consumers, its own cadence, a different team). A submodule boundary is
  a natural "independent version / bump-only-when-changed" boundary, but it adds real cost:
  cross-repo release ordering (the submodule's NuGet must be published before a consumer packs a
  dependency on it) and `Directory.Build.props`/`Directory.Packages.props` upward-search leakage (a
  submodule with no props of its own silently inherits the parent's version). If adopted: group a
  *few* cohesive repos (not 60), place them outside `src/` (e.g. `/modules`), and make each
  self-contained. Deferred — no submodules for now.
  - Note: libraries that already live in a fully **separate repo** (not an in-tree submodule) are a
    different, now-implemented story — see "Module repos" below.
  - **Planned interaction with NuGet publishing:** when submodules arrive, we still want to
    **publish their NuGets from this repo's `publish_nugets.yml`** (one publishing pipeline), but with
    **each submodule owning its own version scope** (its own `Directory.Build.props`/version),
    decoupled from the monorepo's `PubVersion.json`. Implementation note for that day:
    [pub/lib/Publish-NugetPackages.ps1](lib/Publish-NugetPackages.ps1) currently packs every discovered project
    with a single `-p:Version` — it must **exclude submodule projects** (or pack them separately) so
    their independent versions are respected.
- **Full per-package independent versioning.** "Bump only the changed package" in a monorepo
  requires a dependency-graph change-detection engine (changed set = directly-changed ∪ all
  transitive dependents) plus per-package tags. High complexity/risk. The suite-level granularity
  above captures most of the benefit; only revisit this if that proves insufficient.

## How to release (current flow)

1. **Refresh our own package pins.** A few of our libraries live in their own repos and reach the
   apps as `PackageReference`, not `ProjectReference` — today `VpnHood.AppLib.Assets.ClassicSpa`
   (the SPA, published from `VpnHood.Client.WebUI`), `VpnHood.Core.Quic.MsQuic.AndroidNative` (the
   prebuilt `libmsquic.so`, published from the msquic fork on every push to its `main`), and the
   `Assets.*` data packages. They are **pinned to an exact version**, so a newly published one does
   not reach a build until someone edits the pin. Publishing without that edit ships the *previous*
   library with new app code, silently and successfully.

   Check each against nuget.org before every release:

   ```bash
   curl -s https://api.nuget.org/v3-flatcontainer/<package-id-lowercased>/index.json
   ```

   The SPA hides this better than the rest: a developer with `.user/use-local-spa.txt` builds
   against the freshly built `spa.zip` and never restores the package at all, so a stale pin looks
   correct locally and only reaches the real world through CI. Verify the way CI sees it, without
   moving that file:

   ```bash
   dotnet restore src/Apps/Client/VpnHood.App.Client.csproj --force -p:VhUserDir=<an empty dir>
   grep -o '"VpnHood.AppLib.Assets.ClassicSpa/[0-9.]*"' src/Apps/Client/obj/project.assets.json
   ```

2. Maintain the CHANGELOG **by hand**: put the next release's notes under a leading `# Latest`
   heading in `CHANGELOG.md` (server notes go in `CHANGELOG.Server.md`). Lines route themselves with
   **trailing tags**: `#client` / `#connect` pick the product; `#android #ios #windows #linux` limit
   platforms (inclusive — no platform tag = every platform, `#android #ios` ships to both); `#store`
   marks the few lines for Google Play's short per-release note (Play caps it at 500 characters).
   CI never rewrites the changelog — at release time the first H1 (`#`) section becomes the GitHub
   release note (the other product's lines dropped, tags stripped), and the **store release notes**
   are generated from the same section by each store repo's `update-release-notes.yml` (extract →
   vhtranslator → fastlane; see the store pipeline README in VpnHood.Client.WebUI `e2e/store/`) —
   run it after editing the section, before publishing. Update the `# Latest` section yourself each
   cycle. Commit + push as normal work.
3. Run `pub/Client/PublishByGithub.ps1` (or `pub/Connect/PublishByGithub.ps1`). It prompts for the
   channel and the Play audience ratio, dispatches **Bump Version** (`bump.yml`) here, waits for it,
   then dispatches that app's publish workflow — which lives in its brand repo — against the freshly
   bumped `develop`. A failed bump means nothing is published.
4. To bump without publishing, run `bump.yml` directly. Choose `prerelease` on/off and optionally
   tick `then_publish_nugets`. It bumps the version once (`PubVersion.json` + `Directory.Build.props`)
   and pushes `develop` (a stable bump also fast-forwards `main`; a prerelease bump does not). It does
   **not** touch the changelog, and it does **not** chain an app publish — every app now publishes
   from its own brand repo, which this repo deliberately holds no credential to trigger.

`pub/Client/Publish.ps1` is now **build-only** for local smoke testing (no bump, no distribute, no
push).

### The channel is asserted, never chosen twice

`pub/PubVersion.json` **decides** the channel: the Play track, the TestFlight-vs-App-Store lane and
the GitHub release flag all read `Prerelease` from it. The `release_type` input on
[publish_app.yml](../.github/workflows/publish_app.yml) does not set anything — it **asserts** what
the dispatcher believed, and the run dies in a ten-second job if the two disagree, instead of
quietly shipping an alpha to production.

Both brand-repo callers forward it, and `PublishByGithub.ps1` fills it from the channel it already
prompted for, so a normal release never asks twice and can never answer inconsistently. Only a
**hand-dispatch from the Actions tab** has to pick it, and the input defaults to `prerelease`
because that is the harmless direction to get wrong: a mistaken prerelease reaches testers, a
mistaken release reaches production.

The input stays *optional* on `publish_app.yml` itself because that workflow is the public entry
point third parties pin (`@v1`); making it required would break every existing caller. Undeclared,
the run warns —

```
Release type not declared
No release_type was passed; shipping as 'release' per pub/PubVersion.json (v8.1.847).
```

— which is the gate telling you it is inert for that run, not an error. A fork sees this only if it
dispatches by hand or its caller does not forward the input.

### Renaming a release asset (keep the old name for a grace period)

GitHub serves a release asset strictly by file name, so renaming one silently breaks every
`…/releases/latest/download/<old-name>` link already out in the world — bookmarks, forks, and
anything polling the file on a schedule. The release itself publishes green; only the far end
notices.

So when an asset is renamed, keep publishing the **old name alongside the new one** for about three
months. [pub/lib/LegacyAssetAliases.ps1](lib/LegacyAssetAliases.ps1) holds the rename map and the
expiry date; [Publish-AndroidApp.ps1](lib/Publish-AndroidApp.ps1) writes the aliased file and
[Publish-GithubRelease.ps1](lib/Publish-GithubRelease.ps1) attaches it. Past the expiry both calls
go inert on their own and the release goes back to the new names only — then delete the file and its
two call sites. The alias carries the *current* release's payload, so a stale poller is handed the
latest build under its real name.

Alias the update-info **JSON** only, never the package. The alias is a *pointer*, not a second copy
of the release: it carries the current release's payload, so its `PackageUrl` already names the real
(new-name) package. Anything still on the old URL downloads the latest build from its canonical
location, and duplicating tens of megabytes per release under a retired name buys nothing.

The alias only reaches releases published *after* it is added. Patch the releases already out —
whichever tag each repo currently serves as **Latest**, since that is what `latest/download`
resolves to:

```bash
gh release download <tag> --repo <owner/repo> --pattern "<new-name>.json"
cp <new-name>.json <old-name>.json
gh release upload <tag> --repo <owner/repo> <old-name>.json --clobber
```

Currently aliased: the Android arm64 web build, renamed from `…-android-arm64-web` to
`…-android-web-arm64` in v8.1.838 (client) / v8.1.847 (connect), aliased until 2026-12-01. Note this
one never affected our own apps — both the universal and the arm64 APK are built from the same
`*.Android.Web` project, whose `AppConfigs.UpdateInfoUrl` has always named
`VpnHood<App>-Android-web.json`.

### NuGet smoke test (validate the pipeline without burning a version)

To prove the pack + push path works against nuget.org **without** consuming a real version, dispatch
**Publish NuGet Packages** with the **`smoke`** input ticked. It publishes throwaway *prerelease*
packages versioned `X.Y.Z.<run_number>-prerelease` (the 4th revision segment is `github.run_number`,
so every run is unique and monotonic — nuget.org never rejects a duplicate). The base
`Major.Minor.Build` in [pub/PubVersion.json](PubVersion.json) is **untouched** and nothing is
committed — that is why a 4th segment is used instead of bumping the real version. Consumers never
pick these up unless they explicitly opt into prerelease.

Locally: `pwsh pub/lib/Publish-NugetPackages.ps1 -smoke` (revision defaults to an `MMddHHmm` timestamp;
override with `-revision <n>`). Implemented directly in
[pub/lib/Publish-NugetPackages.ps1](lib/Publish-NugetPackages.ps1): it packs with
`-p:Version=X.Y.Z.<revision>-prerelease` instead of the stable `X.Y.Z`.

### Which projects are published (packable discovery)

[pub/lib/Publish-NugetPackages.ps1](lib/Publish-NugetPackages.ps1) **discovers** the packages to publish
instead of carrying a hand-maintained list: it globs `src/**/*.csproj` and packs every project that
does **not** opt out with `<IsPackable>false</IsPackable>` — the standard .NET convention. Apps under
`src/Apps` and the `VpnHood.AppLib.Swagger` stub declare `IsPackable=false`; every library under
`src/Core` and `src/AppLib` is packable by default. To publish a new library, just add it — no script
edit. To keep one out, set `IsPackable=false` on it.

This replaced ~48 identical per-project `_publish.ps1` forwarder scripts and the explicit list that
lived in `Publish-NugetPackages.ps1`. That list had silently drifted (a trailing-dot path typo that only
failed on Linux CI *after* real packages had been pushed, and two packable libraries —
`VpnHood.AppLib.Linux.Common` and `VpnHood.AppLib.Ios.Common` — that were never being published);
discovery makes that class of bug impossible. Per-app build scripts (`src/Apps/*/_publish.ps1`) are
unrelated and remain — they are real build logic invoked directly by the app CI workflows.

### Build environment, speed, and the publishing gate

- **Windows runner + workloads.** The packable suite spans `net10.0`, `net10.0-android`,
  `net10.0-windows` (incl. the WPF library `VpnHood.AppLib.Win.Common.WpfSpa`) and `net11.0-ios`.
  Only a Windows host can build the Windows/WPF projects, so `publish_nugets.yml` runs on
  `windows-latest`, installs the `android`+`ios` workloads, and installs the **.NET 11 preview** SDK
  (the `net11.0-ios` libraries need it; `global.json` `rollForward=latestMajor` then selects it).
- **One parallel pack pass.** The orchestrator writes a throwaway solution (`_nuget_pack.slnx` at the
  repo root — git-ignored, removed in a `finally`) containing exactly the discovered packable
  projects, then runs a single `dotnet pack` on it: MSBuild builds shared dependencies once and packs
  the projects in parallel, after which every produced `.nupkg` (and its `.snupkg`) is pushed. This
  replaced ~49 sequential per-project `dotnet pack` processes (and the old `pub/lib/PublishNuget.ps1`),
  cutting the pack phase from ~6 min to ~2.5 min. A build failure in any project fails the whole pack
  (it never half-publishes) and MSBuild names the culprit.
- **Publishing is gated to the `vpnhood` org.** The publish job has `if:
  github.repository_owner == 'vpnhood'`, so forks skip it entirely and never push the shared package
  IDs. Inside the org a missing `NUGET_API_KEY` is a hard error (the publish throws) — no warn-and-skip.

## Server release (done — same split as Connect)

The **server** releases to a **separate repo** (`vpnhood/VpnHood.App.Server`), the same split as
Connect: the server's code + version live here in the monorepo, but the release is produced by
`server_publish.yml` **in that repo**, which checks out this monorepo at build time. Because the
workflow runs inside the target repo it creates the release with the automatic `github.token` — no
cross-repo PAT. It funnels through the shared `pub/lib/Publish-GithubRelease.ps1` (with `-assetSet server`
and `-changelogFileName CHANGELOG.Server.md`), so one release creator serves every product.

- **Branches.** The server release repo has only a **`main`** branch — there is no code there, so a
  `develop` line would be meaningless. The `develop → main` prerelease/stable model lives HERE in the
  monorepo (§2): a prerelease server release *builds from* `develop`; a stable one first fast-forwards
  `main` via `bump.yml`. The server workflow just builds from whatever monorepo ref it is handed.
- **No store, no fastlane.** The only "store" is Docker Hub. One `ubuntu-latest` job builds both the
  Linux packages and the Windows-x64 zip (the server has no MSI/Advanced-Installer step, so Windows
  cross-builds on Linux); a second job pushes the multi-arch image (skip-with-warning if the
  `DOCKERHUB_*` secrets are absent).
- **Trigger.** `pub/Server/PublishByGithub.ps1` bumps this monorepo (publish/nuget OFF), waits, then
  dispatches `server_publish.yml`. `pub/Server/Publish.ps1` is now build-only for local smoke tests
  (no bump, no distribute, no push) — distribution is CI-only, matching Client/Connect.

Design + validation notes: [docs/cicd/server-publishing.md](../docs/cicd/server-publishing.md).

## Module repos — separate library repos publishing their own NuGets

Some vpnhood libraries live in their own repos ("module repos", e.g. `VpnHood.Core.Proxies`) and
ship their own NuGets on their own cadence — while staying **version-aligned** with the monorepo.
They all publish through ONE shared cross-repo module in this repo, so the logic exists once:

- [.github/workflows/publish_module_nugets.yml](../.github/workflows/publish_module_nugets.yml) —
  reusable workflow the module repo calls with ~10 lines
  (`uses: vpnhood/VpnHood/.github/workflows/publish_module_nugets.yml@develop`, `secrets: inherit`,
  `permissions: contents: write`). Internal repos pin `@develop` (lockstep — same rationale as the
  `publish_app.yml` callers). This is **not** part of the forker/skeleton contract: forkers consume
  the published NuGets; they never call this.
- [pub/lib/Publish-ModuleNugetPackages.ps1](lib/Publish-ModuleNugetPackages.ps1) — the logic. **Version rule:**
  read the monorepo version — **always from `develop`** (develop always carries the highest
  version); if it is ahead of the module's own `pub/PubVersion.json`, **adopt** it, otherwise
  **bump the module's own build number** (the module may run ahead; the next monorepo bump
  leapfrogs and re-syncs). Then pack every packable project (same `IsPackable` opt-out discovery
  as `publish_nugets.yml`) and push — **a stable `X.Y.Z`**, per rule #3 above (prerelease lines
  are an app concept). A manual-only `prerelease` input exists as an escape hatch (appends
  `-prerelease`; the bump still commits, so the next stable publish just takes the next build
  number) — the normal flow never uses it. CI commits the bump back to the dispatched branch
  (CI-owned bump, like `bump.yml` here).

To onboard a module repo: add `pub/PubVersion.json` (`{Version, BumpTime}`, lowercase `pub/` —
the same layout convention as the monorepo), a root
`Directory.Build.props` carrying the single `<Version>` (remove per-csproj `<Version>`s so it
applies), `IsPackable=false` on non-library projects, and the small `publish_nugets.yml`
dispatcher — see `VpnHood.Core.Proxies` for the reference shape. Optionally a root `_publish.ps1`
one-shot trigger (commit pending work → pull → push → `gh workflow run publish_nugets.yml`) so a
publish is a single local command; the CI still does all the real work.

**Step-by-step runbook, including the version-rule table and the onboarding gotchas (per-csproj
`<Version>` overriding the props stamp, the `IsPackable` regex not tolerating a `Condition`, the
silent org gate, pwsh-7-only encoding): [pub/MODULE-REPOS.md](MODULE-REPOS.md).**

**Shipping an executable instead of a library — a `VpnHood.Tools.*` .NET tool on its own version
line, published with Trusted Publishing: [pub/TOOL-REPOS.md](TOOL-REPOS.md).**

## Next steps (not yet implemented)

1. **(Later) `modules/` folder** for any library that earns independent versioning/consumers — as
   git submodules, each self-contained (own props), released on its own cadence.
2. **(Optional) Adopt CPM** as an isolated follow-up PR.

## What changed in this pass

- **CI-owned bump**: new [pub/Invoke-VersionBump.ps1](Invoke-VersionBump.ps1) + [.github/workflows/bump.yml](../.github/workflows/bump.yml).
- **Standalone NuGet publishing**: new [.github/workflows/publish_nugets.yml](../.github/workflows/publish_nugets.yml).
- [pub/lib/Common.ps1](lib/Common.ps1) — the local commit-to-main helpers were removed; `pub/Invoke-VersionBump.ps1`
  owns the push to `develop` (and the fast-forward to `main` on a stable bump, no `--force`).
- **NuGet publish is one parallel pack pass** ([pub/lib/Publish-NugetPackages.ps1](lib/Publish-NugetPackages.ps1))
  driven by `IsPackable` discovery; the old per-project `pub/lib/PublishNuget.ps1` was removed. Version
  is stable `X.Y.Z` except under the `smoke` input.
- [pub/Client/Publish.ps1](Client/Publish.ps1) — build-only (removed bump/distribute/push).
- [pub/Client/Publish-GithubRelease.ps1](Client/Publish-GithubRelease.ps1) — no longer stamps the changelog or
  commits/pushes (the bump step owns that); it only reads the changelog and creates the release.
- **Renamed `pub/Core` → `pub/lib`** (the shared publish-script library) and updated all references.
- **Deleted `pub/Android.GooglePlay`** — the obsolete pre-Fastlane manual APK→release uploader.
- This document.
