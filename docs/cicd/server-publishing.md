# Server CI/CD publishing

Status: **implemented (pending first CI validation run).** The **VpnHood Server** release now runs on
GitHub Actions, mirroring Client/Connect. The design below is the original plan; the "As built"
section records where the final implementation differs from it. See
[Pub/RELEASE-STRATEGY.md](../../Pub/RELEASE-STRATEGY.md) "Server release".

## As built (deltas from the plan below)

- **Architecture:** mirror Connect — `server_publish.yml` lives in `vpnhood/VpnHood.App.Server`,
  checks out the monorepo into `code/`, releases to itself with `github.token`. No cross-repo PAT.
- **Branches:** the server release repo has **`main` only** (no `develop` — no code there). The
  `develop → main` model stays in the monorepo (`bump.yml`).
- **One build job, not two.** The server Windows package is a self-contained `dotnet publish -r
  win-x64` + `Compress-Archive` (no MSI/Advanced Installer), so it **cross-builds on `ubuntu-latest`**
  alongside Linux — the planned separate `windows-latest` job was collapsed away.
- **Release script:** decision **A** — the shared `Pub/Lib/PublishToGithub.ps1` gained
  `-changelogFileName` + `-assetSet` (`app`|`server`); the standalone `Pub/Server/PublishToGitHub.ps1`
  was deleted. One release creator for all three products.
- **Docker:** `docker/build-push-action` (multi-arch) → Docker Hub, gated on `DOCKERHUB_*` +
  `push_docker`; skip-with-warning when absent. `publish_docker.ps1` gained `-generateOnly` to emit
  the compose files in CI without a local image build.
- **Files:** `Pub/Server/Publish.ps1` (build-only), new `Pub/Server/PublishByGithub.ps1`,
  `Src/Apps/Server.Net/_publish.ps1` (env-resolved release URL). Secrets doc: `.github/DEPLOYMENT.md`.
- **Not yet done:** push `server_publish.yml` to the server repo's `main`, set the Docker Hub secrets,
  and run the validation sequence at the bottom of this doc. Nothing has been pushed or dispatched.

---

## Original plan

This was the design for moving the server release off a developer's machine and onto GitHub Actions,
matching how Client and Connect already work.

## Goal (in the user's words)

- **Build the server offline** (on a dev machine) for a quick smoke test — keep this working.
- **Never publish from offline** — the GitHub release + Docker push happen only in CI, like Client
  and Connect. A local build is a smoke test, not a distribution.

That is exactly the rule the rest of the pipeline already follows: *all builds and all publishing
run on GitHub Actions; local scripts are build-only.*

## How the two existing shapes work (recap)

| | Client | Connect | **Server (this plan)** |
|---|---|---|---|
| Code lives in | this monorepo | this monorepo | this monorepo |
| Release repo | this repo | `vpnhood/Vpnhood.App.Connect` | `vpnhood/VpnHood.App.Server` |
| Workflow lives in | this repo — [publish_client.yml](../../.github/workflows/publish_client.yml) | the Connect repo — `connect_publish.yml` | **the Server repo — `server_publish.yml`** |
| Token to create the release | automatic `github.token` (same-repo) | automatic `github.token` (workflow runs *in* the target repo) | **automatic `github.token`** (same trick) |
| Trigger script | [Client/PublishByGithub.ps1](../../Pub/Client/PublishByGithub.ps1) | [Connect/PublishByGithub.ps1](../../Pub/Connect/PublishByGithub.ps1) | **new `Pub/Server/PublishByGithub.ps1`** |
| Store leg | Google Play / App Store | Google Play / App Store | **none** (no fastlane, no store) |
| Extra artifact | — | — | **multi-arch Docker image → Docker Hub** |

### Why mirror Connect (the chosen architecture)

The server already releases to a **separate** repo — [Pub/Server/PublishToGitHub.ps1](../../Pub/Server/PublishToGitHub.ps1)
hardcodes `vpnhood/VpnHood.App.Server` (a release-only repo: no source, just releases + install
scripts + docker-compose). Connect solved "release to another repo" by running its workflow **inside**
that repo, so the automatic `github.token` is enough — **no long-lived cross-repo PAT.** We copy that:

- `server_publish.yml` lives in `vpnhood/VpnHood.App.Server`.
- It checks out **this monorepo** at the bumped ref (`vpnhood/VpnHood`, `ref=develop`), builds, and
  creates the release **in itself**.
- Least-privilege token story, and consistent with the pattern a contributor already has to learn
  for Connect.

## What CI must produce (the server's release assets)

From the current local flow ([Server/PublishToGitHub.ps1](../../Pub/Server/PublishToGitHub.ps1) +
[Server/_publish.ps1](../../Src/Apps/Server.Net/_publish.ps1) +
[publish_docker.ps1](../../Src/Apps/Server.Net/Pub/publish_docker.ps1)):

- **Linux** (`linux-x64`, `linux-arm64`, `linux-any`): `.tar.gz`, install `.sh`, update `.json`,
  plus the msquic variant script. Built with plain `dotnet publish` on `ubuntu-latest`.
- **Windows** (`win-x64`): `.zip`, install `.ps1`, update `.json`. Built on `windows-latest` (or
  cross-published from Linux — see open questions).
- **Docker**: multi-arch (`linux/amd64` + `linux/arm64`) image `vpnhood/vpnhoodserver:<tag>`
  (+ `:latest` on a stable release), plus the two compose helper files
  (`VpnHoodServer.docker.yml`, `VpnHoodServer.docker.sh`) attached to the GitHub release.

## Proposed workflow: `server_publish.yml` (in the Server repo)

Shape mirrors `publish_client.yml`: parallel build jobs → a final `release` job that is the **only**
GitHub writer and runs last. No store jobs.

```yaml
on: workflow_dispatch
  inputs:
    ref              # monorepo ref to build (default: develop)
    publish_release  # create the GitHub release (default false — dry-run builds otherwise)
    push_docker      # push the multi-arch image to Docker Hub (default true)

jobs:
  build-linux     (ubuntu-latest)   -> artifact: server-linux
  build-windows   (windows-latest)  -> artifact: server-windows
  build-docker    (ubuntu-latest)   -> docker/build-push-action, multi-arch, --push to Docker Hub
  release         (ubuntu-latest, needs: [build-linux, build-windows, build-docker])
                  # downloads artifacts, reassembles Pub/bin/<tag>/VpnHoodServer,
                  # creates the release in THIS (server) repo via the shared release script.
```

Each job:
1. `actions/checkout@v5` of **`vpnhood/VpnHood`** at `${{ inputs.ref }}` (the monorepo — this repo
   holds only the workflow + release assets).
2. `actions/setup-dotnet@v5` with `global-json-file: global.json` (server targets `net10.0`; the
   Docker build drops `global.json` and uses SDK 10.x as it already does).
3. Write the `../.user` credential **stub** (`nuget_api_key.txt`) so `Common.ps1` loads — same
   pattern every client job uses.
4. Run the existing `Src/Apps/Server.Net/_publish.ps1` (Linux+Windows) unchanged.

### Docker in CI (the one real behavior change)

Today the multi-arch image is `docker buildx … --push`ed **from a laptop** using local Docker Hub
creds ([publish_docker.ps1](../../Src/Apps/Server.Net/Pub/publish_docker.ps1) `-distribute 1`). In
CI:

- Use `docker/setup-qemu-action` + `docker/setup-buildx-action` + `docker/build-push-action` (CI
  does multi-arch natively — no manual QEMU/`binfmt` juggling, no dedicated `vhbuilder`).
- `docker/login-action` with **new secrets** `DOCKERHUB_USERNAME` + `DOCKERHUB_TOKEN`.
- Tags: `vpnhood/vpnhoodserver:<versionTag>`, plus `:latest` when the release is stable (`isLatest`).
- The `Dockerfile` is unchanged (it already builds portable IL for both arches).

Registry decision: **Docker Hub only** — it is the image users already pull; install scripts and
compose keep pointing there, no consumer-facing change.

## Local script changes (keep offline build, drop offline publish)

- [Pub/Server/Publish.ps1](../../Pub/Server/Publish.ps1): **remove the `-distribute` path.** It
  becomes build-only for smoke tests, matching the note already in the file ("Remove the distribute
  path once server_publish.yml exists") and mirroring what was done to `Pub/Client/Publish.ps1`.
  `-docker 1` still does a **local host-arch** `buildx … --load` (no push) for smoke testing — this
  is the "build offline" path.
- [Pub/Server/PublishToGitHub.ps1](../../Pub/Server/PublishToGitHub.ps1): **retire in favor of the
  shared** [Pub/Lib/PublishToGithub.ps1](../../Pub/Lib/PublishToGithub.ps1) (per RELEASE-STRATEGY
  next-step #1), so all three products create releases through one script. The shared script's asset
  list is currently client/connect-specific (Android/MSI), so it must be made **asset-set aware** —
  see the decision below.
- New `Pub/Server/PublishByGithub.ps1`: a trimmed copy of
  [Pub/Connect/PublishByGithub.ps1](../../Pub/Connect/PublishByGithub.ps1) — bump the monorepo
  (publish OFF, nuget OFF), wait, then dispatch `server_publish.yml` in the Server repo. No
  rollout/store prompts; add a `-pushDocker` switch. Only prompt: release vs prerelease.

### Decision needed: how the shared release script learns the server's assets

The shared [Pub/Lib/PublishToGithub.ps1](../../Pub/Lib/PublishToGithub.ps1) hardcodes the
Android/Linux/Windows **client** asset list. Two clean ways to include the server's different set
(linux tar.gz + win-x64 zip + docker compose files, no Android/MSI):

- **A. Parameterize the asset list.** Add a `-product server|client|connect` (or an explicit
  `-assets` array) so each product supplies its own set; one release creator for all three. Most in
  the spirit of "route server through the shared script". *(Recommended.)*
- **B. Keep a thin `Pub/Server/PublishToGithub.ps1`** that only computes the server asset list, then
  calls the shared script for the generic delete-old / create-release / release-note logic.

Both end at one release-creation code path; A is less indirection.

## New secrets (add to [.github/DEPLOYMENT.md](../../.github/DEPLOYMENT.md))

Set on the **`vpnhood/VpnHood.App.Server`** repo (where the workflow runs):

| Secret | Required? | What it is |
|---|---|---|
| `GITHUB_TOKEN` | automatic | Creates the release in the Server repo. No setup. |
| `DOCKERHUB_USERNAME` | Required for Docker push | Docker Hub account/org that owns `vpnhood/vpnhoodserver`. |
| `DOCKERHUB_TOKEN` | Required for Docker push | Docker Hub **access token** (not the password), Read/Write/Delete on the repo. |

If the Docker secrets are absent, `build-docker` should **skip-with-warning** (stay green) and the
release still ships the Linux/Windows assets — same fork-friendly rule as every store leg today.

Because the workflow lives in the Server repo, it also needs to check out the monorepo. `vpnhood/VpnHood`
is public, so a plain `actions/checkout` works with no token (Connect already does this).

## Trigger flow (end to end)

```text
Pub/Server/PublishByGithub.ps1
  1. gh workflow run bump.yml   --repo vpnhood/VpnHood            (prerelease?, then_publish=false, nugets=false)
     -> waits for the bump to finish (version pushed to develop [+ main if stable])
  2. gh workflow run server_publish.yml --repo vpnhood/VpnHood.App.Server --ref main
       -f ref=develop -f publish_release=true -f push_docker=true
     -> checks out monorepo@develop, builds linux+windows+docker, pushes image,
        creates the release in vpnhood/VpnHood.App.Server
```

Version stays single-sourced: the server shares the monorepo `Pub/PubVersion.json`; only `bump.yml`
ever writes it. Release notes come from `CHANGELOG.Server.md` (hand-maintained; CI only reads it).

## Files this will touch

- **New** `.github/workflows/server_publish.yml` — but it must live in the **Server** repo, not
  here. (Keep a copy/reference in this repo's docs; the authoritative file ships to `VpnHood.App.Server`.)
- **New** `Pub/Server/PublishByGithub.ps1`.
- **Edit** `Pub/Server/Publish.ps1` — delete the `-distribute` branch (build-only).
- **Edit/retire** `Pub/Server/PublishToGitHub.ps1` → shared `Pub/Lib/PublishToGithub.ps1` (per the
  asset-set decision above).
- **Edit** `Pub/RELEASE-STRATEGY.md` — move "Server CI publish" from *Next steps* to *done*.
- **Edit** `.github/DEPLOYMENT.md` — add the Docker Hub secrets + a Server section.
- **Edit** `Src/Apps/Server.Net/Pub/publish_docker.ps1` — CI path uses `build-push-action`; the
  local path keeps the host-arch `--load` smoke build.

## Open questions before implementing

1. **Windows build host.** Client builds the MSI on `windows-latest` because Advanced Installer needs
   it. The **server** Windows package is just a self-contained `dotnet publish` + zip — it can likely
   be **cross-published from `ubuntu-latest`** (`-r win-x64`), collapsing `build-windows` into the
   Linux job and saving a runner. Confirm `_publish.ps1`'s Windows path cross-builds cleanly on Linux.
2. **Does `VpnHood.App.Server` already have Actions enabled / the workflow indexed?** Like Connect,
   the dispatch 404s until the workflow file is pushed to its default branch and indexed (see
   DEPLOYMENT.md "Activating the workflows"). `PublishByGithub.ps1` should pre-check this.
3. **Asset-set decision A vs B** above.
4. **`isLatest`/`:latest` semantics** for prereleases — keep current behavior (only stable tags
   `:latest`).

## Validation plan (mirrors how Client was proven)

1. Dry-run: dispatch `server_publish.yml` with `publish_release=false`, `push_docker=false` against a
   throwaway fork of the Server repo — confirm Linux/Windows artifacts build and the multi-arch image
   *builds* (no push).
2. Add Docker Hub secrets to the fork; re-run with `push_docker=true` to a throwaway Docker Hub repo.
3. Full run with `publish_release=true` on the fork; verify all assets attach and the compose files
   are present.
4. Only then wire `Pub/Server/PublishByGithub.ps1` against the real repos.
