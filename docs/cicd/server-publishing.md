# Server CI/CD publishing

How the **VpnHood Server** is released. Live since 2026-07; the most recent releases were produced
this way. Companion to [pub/RELEASE-STRATEGY.md](../../pub/RELEASE-STRATEGY.md) ("Server release")
and [deployment.md](deployment.md) (the secrets).

Same rule as every other product here: **all publishing runs on GitHub Actions, never from a
developer machine.** A local build is a smoke test, not a distribution.

## Architecture

The server's code and version live in this monorepo, but the release is produced by
`server_publish.yml` **inside `vpnhood/VpnHood.App.Server`** — the same split Connect uses, and for
the same reason: a workflow running in the target repo can create a release there with the automatic
`github.token`, so **no cross-repo PAT exists anywhere in the chain**.

| | How it is set up |
| --- | --- |
| Code + version | this monorepo (`pub/PubVersion.json`, written only by `bump.yml`) |
| Workflow + release | `vpnhood/VpnHood.App.Server` (`main` only — there is no `develop`, because there is no code there) |
| Token | automatic `github.token` of the server repo |
| Release notes | `CHANGELOG.Server.md` in this monorepo — hand-maintained, CI only reads it |
| Store legs | none (no fastlane, no app store) |

The workflow checks this monorepo out into `code/` at the requested ref, builds, and creates the
release in itself.

## What a release ships

- **Linux** (`linux-x64`, `linux-arm64`, `linux-any`): `.tar.gz`, install `.sh`, update `.json`, plus
  the msquic variant script.
- **Windows** (`win-x64`): `.zip`, install `.ps1`, update `.json`. The server package is a
  self-contained `dotnet publish` + zip with no installer, so it **cross-builds on `ubuntu-latest`**
  alongside Linux — one build job, no Windows runner.
- **Docker**: a multi-arch (`linux/amd64` + `linux/arm64`) image pushed to Docker Hub as
  `vpnhood/vpnhoodserver:<tag>`, plus `:latest` on a stable release; the two compose helper files
  (`VpnHoodServer.docker.yml`, `VpnHoodServer.docker.sh`) are attached to the GitHub release.

Docker is built with `docker/build-push-action` (multi-arch natively — no QEMU juggling) and gated on
both the `DOCKERHUB_*` secrets and the `push_docker` input; absent secrets **skip with a warning** and
the release still ships the Linux/Windows assets, the same fork-friendly rule as every store leg.
`src/Apps/Server.Net/pub/publish_docker.ps1 -generateOnly` emits the compose files in CI without
building an image.

All three products create their GitHub release through the **one shared**
[pub/lib/Publish-GithubRelease.ps1](../../pub/lib/Publish-GithubRelease.ps1), which takes
`-changelogFileName` and `-assetSet` (`app` | `server`) to cover the differing asset lists. The
standalone server release script was deleted.

## Triggering a release

```text
pub/Server/PublishByGithub.ps1
  1. gh workflow run bump.yml  --repo vpnhood/VpnHood
       (prerelease?, then_publish=false, then_publish_nugets=false)  → waits for it to finish
  2. gh workflow run server_publish.yml --repo vpnhood/VpnHood.App.Server --ref main
       -f ref=develop -f publish_release=true -f push_docker=<true|false>
```

The version is single-sourced: the server shares the monorepo's `pub/PubVersion.json`, and only
`bump.yml` ever writes it.

[pub/Server/Publish.ps1](../../pub/Server/Publish.ps1) is **build-only** for smoke tests; `-docker 1`
does a local host-arch `buildx … --load` with no push.

## Secrets

Set on **`vpnhood/VpnHood.App.Server`** (where the workflow runs). Full descriptions, plus the
`DOCKER_IMAGE` / `CODE_REPO` variables a fork would set, are in
[deployment.md](deployment.md#server-separate-repo--vpnhoodvpnhoodappserver).

| Secret | Required? | What it is |
| --- | --- | --- |
| `GITHUB_TOKEN` | automatic | Creates the release in the server repo. No setup. |
| `DOCKERHUB_USERNAME` | for the Docker push | Docker Hub account/org that owns the image. |
| `DOCKERHUB_TOKEN` | for the Docker push | Docker Hub **access token** (not the password). |

The workflow also checks out this monorepo; `vpnhood/VpnHood` is public, so plain `actions/checkout`
needs no token.

## Gotcha

Like every dispatch-triggered workflow here, the dispatch **404s until the workflow file has been
pushed to the server repo's default branch and indexed** by GitHub — see
[deployment.md](deployment.md) "Activating the workflows". `PublishByGithub.ps1` pre-checks this and
fails with that instruction rather than a bare 404.
