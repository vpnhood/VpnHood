# CI/CD Handoff — VpnHood-deploytest

> Working notes to continue the GitHub Actions CI/CD migration in this repo.
> This is a **test fork** (`vpnhood/VpnHood-deploytest`), public, owned by the `vpnhood`
> org so it can use **org-level Actions secrets** (a personal fork cannot). Use it to
> validate publishing workflows before they land on `vpnhood/VpnHood`.
>
> _Last updated: 2026-06-30._

---

## 1. Repo & remote setup

| Thing | Value |
|---|---|
| Local path | `C:\Users\Developer\source\repos\Vh\VpnHood-deploytest` |
| `origin` | `https://trudyhood@github.com/vpnhood/VpnHood-deploytest.git` (this test fork) |
| `upstream` | `https://trudyhood@github.com/vpnhood/VpnHood.git` (real project) |
| Current branch | `development` (tracks `origin/development`; in sync with `upstream/development` @ `2e020c776`) |
| git identity (repo-local) | `user.name = Trudy`, `user.email = trudy@vpnhood.com` |

The pristine upstream clone lives next door at `C:\Users\Developer\source\repos\Vh\VpnHood`
(origin = `vpnhood/VpnHood` only). Do real/production work there; do CI experiments here.

`workflow_dispatch`-triggered workflows must exist on the repo's **default branch** to be
launchable from the Actions UI, so push test workflow changes to the branch the UI reads.

---

## 2. PENDING — uncommitted change in this working copy

The secret/env var was renamed **`PLAY_JSON_KEY` → `GOOGLE_PLAY_APIKEY`** (uppercase, no
abbreviation — house style). It is currently **uncommitted** here. 9 sites across 4 files:

- `fastlane/Fastfile` — 5× `json_key_data: ENV['GOOGLE_PLAY_APIKEY']` (lines 23, 58, 83, 99, 120)
- `.github/workflows/publish_store_package.yml` — 2× `GOOGLE_PLAY_APIKEY: ${{ secrets.GOOGLE_PLAY_APIKEY }}` (lines 137, 150)
- `.github/workflows/publish_store_contents.yml` — 1× (line 34)
- `.github/DEPLOYMENT.md` — secrets table (line 26) + Play setup note (line ~132)

**To do:** commit this rename here, validate the Play workflow, then port the same rename
to `vpnhood/VpnHood`. (Do not commit until you intend to — original folder still has the old
`PLAY_JSON_KEY` name; keep the two consistent when you finalize.)

After committing, add the matching GitHub secret (see §3). The Actions linter warning
"Context access might be invalid: GOOGLE_PLAY_APIKEY" is harmless and clears once the secret
exists.

---

## 3. `GOOGLE_PLAY_APIKEY` — what it is

- The **whole Google Play service-account JSON file contents** (raw JSON, NOT base64).
- **Shared across all apps**, not per-app: Fastlane (`upload_to_play_store`/`supply`) reads
  the package name from the AAB itself, so one service-account JSON serves every app in the
  same Play developer account.
- Local copy lives **outside the repo** (gitignored sibling), created by the maintainer:
  `C:\Users\Developer\source\repos\Vh\.user\google_play_apikey.json`
  (type `service_account`, project `pc-api-7353326956201132956-83`).
- Google service-account keys **cannot be re-downloaded** — if lost, generate a new key in the
  Play Console / GCP IAM and delete the old one.
- Set the secret with: `gh secret set GOOGLE_PLAY_APIKEY < ../.user/google_play_apikey.json`
  (org-level secret on `vpnhood`, or repo-level on this fork).

> **Security:** this key's private material was once pasted into a now-deleted chat. Rotating
> the key (new key + delete old) is the only full mitigation — do it when convenient.

Alternative considered: **Workload Identity Federation** (keyless OIDC via
`google-github-actions/auth@v2`, `create_credentials_file: true`). Only worth it if org policy
`iam.disableServiceAccountKeyCreation` blocks key creation. Not adopted.

---

## 4. CI/CD migration status (client only; server stays on Docker)

Migrating **client** publishing (Linux, Windows, Android; iOS deferred — no app project yet)
from the local PowerShell pipeline (`Pub/`) to GitHub Actions. CI reuses the existing per-app
`_publish.ps1` scripts unchanged.

**DONE & validated end-to-end** (last full run all green: 7 linux + 3 windows + 6 android = 16
release assets, signed MSI):

- `client_linux_build.yml` — self-contained linux-x64 / linux-arm64, no secrets.
- `client_windows_build.yml` — MSI via Advanced Installer (Caphyon action), licensed by
  `ADVANCED_INSTALLER_LICENSE`. `PublishWinApp.ps1 -stage all|publish|package`.
- `client_android_build.yml` + the `build-android` job in `client_publish.yml` — split into a
  **per-store matrix** (google-aab, web-apk, web-arm64); cut Android wall-clock ~11m → ~5m.
- `client_publish.yml` — aggregate: Linux + Windows + Android build jobs in parallel → one
  `release` job reusing the real `PublishToGithub.ps1`. Repo/token overridable via
  `VH_PUBLISH_REPO` / ambient `GITHUB_TOKEN`.
- **Windows signing** (gated): Microsoft Trusted Signing `sign` CLI, runs **only when all six**
  `AZURE_*` + `VH_SIGN_*` secrets are present, else builds unsigned. Verified signer
  `CN=OmegaHood LLC` (account `Vh-Signing`, profile `OmegaHood-CodeSigning`, endpoint
  `https://wus2.codesigning.azure.net/`). **Never** sign under "NetFreedom Pioneers".
- **Android signing** (gated): `Pub/Lib/PrepareCiAndroidSigning.ps1` materializes real
  keystores from `ANDROID_KEYSTORE_<NAME>_BASE64`/`_PASSWORD`(/`_ALIAS`) for NAME ∈
  {CLIENT_GOOGLE, CLIENT_WEB, CONNECT_GOOGLE, CONNECT_WEB}; else generates an **ephemeral
  throwaway keystore** so a public/test build never needs the production key. Map in
  `Pub/Lib/android-signing.json`.
- **Per-app config refactor**: all non-secret build settings in one external `publish.json` per
  app + per-store `google/`/`web/` subfolders under `.user/`. Full design is in
  `.github/DEPLOYMENT.md` (committed source-of-truth for forkers).

**Play publishing workflows** (`publish_store_package.yml`, `publish_store_contents.yml`,
`fastlane/`): upload AAB to Play, then replace the GitHub release asset with the Play-signed
APK; prerelease → `alpha` track, stable → `production`. These are what the §2 rename touches.

---

## 5. Pending / TODO

1. **Commit the `GOOGLE_PLAY_APIKEY` rename** here, validate, then port to `vpnhood/VpnHood`.
2. **Add `GOOGLE_PLAY_APIKEY` secret** (org `vpnhood` preferred; or this repo) — see §3.
3. **Revert `TEMP-CI-AOT-OFF`** before any production release: `RunAOTCompilation` was set
   `False` in `Client.Android.{Web,Google}` csproj to speed CI. Grep `TEMP-CI-AOT-OFF`, set
   back to `True`.
4. **Add real production Android keystore secrets** on `vpnhood/VpnHood` (CLIENT/CONNECT ×
   GOOGLE/WEB). The Web APK key has **no Play recovery** — never put it in a public/test repo.
5. **Push the 7 Windows/signing secrets to org `vpnhood`** — was blocked: `gh` token lacked
   `admin:org`. Run `gh auth refresh -h github.com -s admin:org` interactively first.
6. **Delete the redundant personal fork** `trudyhood/VpnHood-deploytest` (no unique commits;
   superseded by this org repo). Blocked: token lacks `delete_repo`. Either delete via GitHub
   UI (Settings → Danger Zone) or `gh auth refresh -h github.com -s delete_repo` then
   `gh repo delete trudyhood/VpnHood-deploytest --yes`.
7. Optional: drop the `upstream` remote from this fork if you don't sync through it.

---

## 6. Secrets reference (full list)

Source of truth is **`.github/DEPLOYMENT.md`** (table + per-platform setup). Keep it in sync
whenever a workflow gains/drops a `secrets.*` reference. Summary:

| Secret | Used by | Purpose |
|---|---|---|
| `GITHUB_TOKEN` | all | Auto-provided. |
| `GH_TOKEN` | `publish_store_package.yml` | Optional PAT; falls back to `GITHUB_TOKEN`. |
| `GOOGLE_PLAY_APIKEY` | `publish_store_*.yml` | Play service-account JSON (whole file). Shared. |
| `ADVANCED_INSTALLER_LICENSE` | windows build/publish | Advanced Installer license ID. |
| `AZURE_TENANT_ID` / `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET` | `client_publish.yml` | Trusted Signing SP (all 6 needed to sign). |
| `VH_SIGN_ACCOUNT` / `VH_SIGN_PROFILE` / `VH_SIGN_ENDPOINT` | `client_publish.yml` | Trusted Signing target. |
| `ANDROID_KEYSTORE_{CLIENT,CONNECT}_{GOOGLE,WEB}_BASE64` / `_PASSWORD` (+ opt `_ALIAS`) | android build/publish | Per app+store keystore + password (alias auto-detected). |

---

## 7. Conventions & gotchas (house rules)

- **Never** commit anything from `..\.user\` — real keystores, passwords, access keys,
  appsettings, and `google_play_apikey.json` live there, gitignored, on the maintainer's machine.
- **Never** sign/publish VpnHood under "NetFreedom Pioneers" (signer removed).
- **Never** upload a production app-signing key to a public/test repo — use ephemeral keystores
  in CI.
- Don't echo secret values to a transcript — names/counts only.
- Don't commit until explicitly intended.
- Coding conventions: `.github/copilot-instructions.md` (primary ctors, `.Vhc()` over
  `.ConfigureAwait(false)`, `AsyncLock` not `SemaphoreSlim`, `TestHelper.WorkingPath` for test
  temp, etc.).
- Never hand-edit the generated TypeScript API stub; rebuild the Swagger project / run
  `_recreate-api.ps1`.

---

## 8. Quick commands

```bash
# add the Play key as a secret (org or this repo)
gh secret set GOOGLE_PLAY_APIKEY < ../.user/google_play_apikey.json

# launch a workflow manually (must be on default branch)
gh workflow run client_publish.yml
gh workflow run publish_store_package.yml

# sync this fork with upstream
git fetch upstream && git merge --ff-only upstream/development

# see the pending rename
git diff --stat
```
