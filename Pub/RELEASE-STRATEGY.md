# Release & Versioning Strategy

Developer-facing notes on how VpnHood is versioned and released, why it is set up this way, and
what we deliberately deferred. This is the source of truth for the release-pipeline direction; keep
it updated when the model changes.

## The challenge

The repo is one large solution (~70 projects, ~60 of them NuGet libraries) plus the apps
(Client, Connect, Server). Historically:

1. **One global version** ([Pub/PubVersion.json](PubVersion.json)) is applied to *every* project
   at pack time (`dotnet pack -p:Version=…` in
   [Pub/Lib/PublishNugets.ps1](Lib/PublishNugets.ps1)). Every release re-versions and re-pushes
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
   → [Pub/Bump.ps1](Bump.ps1)) increments `PubVersion.json` (+ `Directory.Build.props`), commits,
   and pushes to `develop` (a stable bump additionally fast-forwards `main`). The CHANGELOG is
   hand-maintained (leading `# Latest` section) and never rewritten by CI. Local machines never bump →
   no cross-developer conflicts. It can optionally chain straight into the client publish and/or NuGet publish. **(Done.)**
2. **`develop` is the prerelease line; `main` is the stable/release line.** [Pub/Bump.ps1](Bump.ps1)
   always pushes `HEAD:develop`; on a **stable** bump it ALSO fast-forwards `HEAD:main` **without
   `--force`** (a **prerelease** bump leaves `main` untouched — prereleases ship to TestFlight / Play
   alpha, not the App Store / Play production). `main` only ever fast-forwards from `develop`, so it is
   a clean fast-forward; a rejection signals a real divergence to reconcile by hand rather than
   overwrite. Protects forkers. **(Done.)**
3. **NuGet is always a stable Release version.** Packing is `-c Release`; the version does not get a
   `-prerelease` suffix from the app flag ([Pub/Lib/PublishNugets.ps1](Lib/PublishNugets.ps1)).
   One clean library version line, decoupled from app prereleases. (The `smoke` input is the only
   exception — see below.) **(Done.)**
4. **One shared version for everything in `src/`.** All projects (apps + libraries) carry the
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
  - **Planned interaction with NuGet publishing:** when submodules arrive, we still want to
    **publish their NuGets from this repo's `publish_nugets.yml`** (one publishing pipeline), but with
    **each submodule owning its own version scope** (its own `Directory.Build.props`/version),
    decoupled from the monorepo's `PubVersion.json`. Implementation note for that day:
    [Pub/Lib/PublishNugets.ps1](Lib/PublishNugets.ps1) currently packs every discovered project
    with a single `-p:Version` — it must **exclude submodule projects** (or pack them separately) so
    their independent versions are respected.
- **Full per-package independent versioning.** "Bump only the changed package" in a monorepo
  requires a dependency-graph change-detection engine (changed set = directly-changed ∪ all
  transitive dependents) plus per-package tags. High complexity/risk. The suite-level granularity
  above captures most of the benefit; only revisit this if that proves insufficient.

## How to release (current flow)

1. Maintain the CHANGELOG **by hand**: put the next release's notes under a leading `# Latest`
   heading in `CHANGELOG.md` (tag lines `#client` / `#connect` so each product's release note is
   filtered correctly; server notes go in `CHANGELOG.Server.md`). CI never rewrites the changelog —
   at release time it extracts whatever the first H1 (`#`) section is for the GitHub release note, and
   Google Play uses the same exclude-phrases pass. Update the `# Latest` section yourself each cycle.
   Commit + push as normal work.
2. Run the **Bump Version** workflow (`bump.yml`). Choose `prerelease` on/off. Optionally tick
   `then_publish` (create the GitHub release) and/or `then_publish_nugets`. It bumps the version once
   (`PubVersion.json` + `Directory.Build.props`) and pushes `develop` (a stable bump also fast-forwards
   `main`; a prerelease bump does not). It does **not** touch the changelog.
3. If you didn't chain them, dispatch **Publish Client** (`publish_client.yml`) and/or **Publish
   NuGet Packages** (`publish_nugets.yml`) against `develop` yourself — both are standalone.

`Pub/Client/Publish.ps1` is now **build-only** for local smoke testing (no bump, no distribute, no
push).

### NuGet smoke test (validate the pipeline without burning a version)

To prove the pack + push path works against nuget.org **without** consuming a real version, dispatch
**Publish NuGet Packages** with the **`smoke`** input ticked. It publishes throwaway *prerelease*
packages versioned `X.Y.Z.<run_number>-prerelease` (the 4th revision segment is `github.run_number`,
so every run is unique and monotonic — nuget.org never rejects a duplicate). The base
`Major.Minor.Build` in [Pub/PubVersion.json](PubVersion.json) is **untouched** and nothing is
committed — that is why a 4th segment is used instead of bumping the real version. Consumers never
pick these up unless they explicitly opt into prerelease.

Locally: `pwsh Pub/Lib/PublishNugets.ps1 -smoke` (revision defaults to an `MMddHHmm` timestamp;
override with `-revision <n>`). Implemented directly in
[Pub/Lib/PublishNugets.ps1](Lib/PublishNugets.ps1): it packs with
`-p:Version=X.Y.Z.<revision>-prerelease` instead of the stable `X.Y.Z`.

### Which projects are published (packable discovery)

[Pub/Lib/PublishNugets.ps1](Lib/PublishNugets.ps1) **discovers** the packages to publish
instead of carrying a hand-maintained list: it globs `Src/**/*.csproj` and packs every project that
does **not** opt out with `<IsPackable>false</IsPackable>` — the standard .NET convention. Apps under
`Src/Apps` and the `VpnHood.AppLib.Swagger` stub declare `IsPackable=false`; every library under
`Src/Core` and `Src/AppLib` is packable by default. To publish a new library, just add it — no script
edit. To keep one out, set `IsPackable=false` on it.

This replaced ~48 identical per-project `_publish.ps1` forwarder scripts and the explicit list that
lived in `PublishNugets.ps1`. That list had silently drifted (a trailing-dot path typo that only
failed on Linux CI *after* real packages had been pushed, and two packable libraries —
`VpnHood.AppLib.Linux.Common` and `VpnHood.AppLib.Ios.Common` — that were never being published);
discovery makes that class of bug impossible. Per-app build scripts (`Src/Apps/*/_publish.ps1`) are
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
  replaced ~49 sequential per-project `dotnet pack` processes (and the old `Pub/Lib/PublishNuget.ps1`),
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
cross-repo PAT. It funnels through the shared `Pub/Lib/PublishToGithub.ps1` (with `-assetSet server`
and `-changelogFileName CHANGELOG.Server.md`), so one release creator serves every product.

- **Branches.** The server release repo has only a **`main`** branch — there is no code there, so a
  `develop` line would be meaningless. The `develop → main` prerelease/stable model lives HERE in the
  monorepo (§2): a prerelease server release *builds from* `develop`; a stable one first fast-forwards
  `main` via `bump.yml`. The server workflow just builds from whatever monorepo ref it is handed.
- **No store, no fastlane.** The only "store" is Docker Hub. One `ubuntu-latest` job builds both the
  Linux packages and the Windows-x64 zip (the server has no MSI/Advanced-Installer step, so Windows
  cross-builds on Linux); a second job pushes the multi-arch image (skip-with-warning if the
  `DOCKERHUB_*` secrets are absent).
- **Trigger.** `Pub/Server/PublishByGithub.ps1` bumps this monorepo (publish/nuget OFF), waits, then
  dispatches `server_publish.yml`. `Pub/Server/Publish.ps1` is now build-only for local smoke tests
  (no bump, no distribute, no push) — distribution is CI-only, matching Client/Connect.

Design + validation notes: [docs/cicd/server-publishing.md](../docs/cicd/server-publishing.md).

## Next steps (not yet implemented)

1. **(Later) `modules/` folder** for any library that earns independent versioning/consumers — as
   git submodules, each self-contained (own props), released on its own cadence.
2. **(Optional) Adopt CPM** as an isolated follow-up PR.

## What changed in this pass

- **CI-owned bump**: new [Pub/Bump.ps1](Bump.ps1) + [.github/workflows/bump.yml](../.github/workflows/bump.yml).
- **Standalone NuGet publishing**: new [.github/workflows/publish_nugets.yml](../.github/workflows/publish_nugets.yml).
- [Pub/Lib/Common.ps1](Lib/Common.ps1) — the local commit-to-main helpers were removed; `Pub/Bump.ps1`
  owns the push to `develop` (and the fast-forward to `main` on a stable bump, no `--force`).
- **NuGet publish is one parallel pack pass** ([Pub/Lib/PublishNugets.ps1](Lib/PublishNugets.ps1))
  driven by `IsPackable` discovery; the old per-project `Pub/Lib/PublishNuget.ps1` was removed. Version
  is stable `X.Y.Z` except under the `smoke` input.
- [Pub/Client/Publish.ps1](Client/Publish.ps1) — build-only (removed bump/distribute/push).
- [Pub/Client/PublishToGithub.ps1](Client/PublishToGithub.ps1) — no longer stamps the changelog or
  commits/pushes (the bump step owns that); it only reads the changelog and creates the release.
- **Renamed `Pub/Core` → `Pub/Lib`** (the shared publish-script library) and updated all references.
- **Deleted `Pub/Android.GooglePlay`** — the obsolete pre-Fastlane manual APK→release uploader.
- This document.
