# Deployment & Required Secrets (for forks)

This repo publishes the VpnHood **client** apps through GitHub Actions. If you fork
the repo and want the publishing workflows to run, you must provide your own
credentials as **repository secrets** — the originals are never committed (they live
outside the repo, in a sibling `../.user/` folder, on the maintainer's machine).

> This document is the source of truth for *what* secrets each workflow needs.
> Keep it in sync whenever a workflow gains or drops a `secrets.*` reference.

## Activating the workflows (fresh fork / import)

If you fork with the **GitHub "Fork" button**, the workflows are copied for you — just enable
Actions on your fork (the **Actions** tab → "I understand my workflows, go ahead and enable them")
and they appear.

If instead you populate a repo by **pushing existing history into a new, empty repo** (a mirror or
`git push` of the whole history), GitHub may show the workflow files in the code tree but **not list
them under Actions**, so they can't be run. This is because GitHub indexes a workflow on the push
whose **diff changes that file** — and an initial bulk-history push whose tip commit doesn't touch
the workflow files leaves them unindexed.

To activate them, make **one push whose diff touches each workflow file** (a comment line is enough),
then they register and become dispatchable:

```bash
# minimal: bump a comment in each workflow you want to activate, then push to the default branch
git commit -am "ci: activate workflows" && git push
# verify GitHub now lists them (expect the full count):
gh api repos/<owner>/<repo>/actions/workflows -q .total_count
```

`pub/Client/PublishByGithub.ps1` also pre-checks this and fails with the same instruction if a
workflow it needs is not yet indexed.

## How to set a secret

```bash
# from inside your fork's working copy
gh secret set SECRET_NAME --body "value"
gh secret set SECRET_NAME < path/to/file        # for file-based values
```
Or in the GitHub UI: **Settings → Secrets and variables → Actions → New repository secret**.

## Secrets at a glance

| Secret | Used by | Required? | What it is |
|---|---|---|---|
| `GITHUB_TOKEN` | all release/publish workflows | Automatic | Provided by GitHub; no action needed. |
| `GOOGLE_PLAY_API_KEY` | `publish_client.yml`, `publish_metadata_googleplay.yml` | Optional (Play) | Google Play service-account JSON (whole file contents). Present → `publish_client.yml` publishes the AAB to Play and attaches the Play-signed APK to the release. Absent → the Play publish is skipped with a warning (the job stays green); nothing is pushed to Google Play. |
| `ADVANCED_INSTALLER_LICENSE` | `publish_client.yml` | Required for Windows | Advanced Installer license ID (used to register AI on the runner). |
| `AZURE_SIGNING_CREDENTIAL` | `publish_client.yml` | Optional (Windows signing) | The single Azure service-principal JSON you download from Azure (contains `AZURE_TENANT_ID`/`AZURE_CLIENT_ID`/`AZURE_CLIENT_SECRET`; other fields ignored). Paste the whole file. Absent → MSI builds unsigned with a warning. |
| `AZURE_SIGNING_TARGET` | `publish_client.yml` | Optional (Windows signing) | Single JSON in Azure Trusted Signing's `metadata.json` schema: `Endpoint`, `CodeSigningAccountName`, `CertificateProfileName`. Not secret and not part of the Azure credential file; required alongside it for signing to run. Store it as a repository **Variable**. |
| `ANDROID_KEYSTORE_GOOGLE_BASE64` / `_PASSWORD` (+ optional `_ALIAS`) | `publish_client.yml` | Optional (Android signing) | Base64 of the keystore that signs the Client Google AAB, plus its store password. The key alias is auto-detected; set `_ALIAS` only for a multi-entry keystore. |
| `ANDROID_KEYSTORE_WEB_BASE64` / `_PASSWORD` (+ optional `_ALIAS`) | `publish_client.yml` | Optional (Android signing) | Base64 of the keystore that signs the Client Web + Web-arm64 APKs, plus its store password. Alias auto-detected; set `_ALIAS` only for a multi-entry keystore. |
| `ANDROID_KEYSTORE_CONNECT_GOOGLE_BASE64` / `_PASSWORD` (+ optional `_ALIAS`) | `publish_client.yml` | Optional (Android signing) | Base64 of the keystore that signs the Connect Google AAB, plus its store password. Alias auto-detected; set `_ALIAS` only for a multi-entry keystore. May reuse the same keystore as Connect Web. |
| `ANDROID_KEYSTORE_CONNECT_WEB_BASE64` / `_PASSWORD` (+ optional `_ALIAS`) | `publish_client.yml` | Optional (Android signing) | Base64 of the keystore that signs the Connect Web APKs, plus its store password. Alias auto-detected; set `_ALIAS` only for a multi-entry keystore. May reuse the same keystore as Connect Google. |
| `APPLE_DISTRIBUTION_CERT_BASE64` / `_PASSWORD` | `publish_client.yml` | Optional (iOS signing) | Base64 of the Apple **Distribution** certificate `.p12` (with private key) that signs the iOS `.ipa`, plus its export password. Absent → the iOS build is UNSIGNED (no `.ipa`, a warning); there is no ephemeral fallback (App Store builds can't self-sign). |
| `IOS_PROVISION_APP_BASE64` | `publish_client.yml` | Optional (iOS signing) | Base64 of the **App Store** provisioning profile for the app (`com.vpnhood.client.ios`). |
| `IOS_PROVISION_EXT_BASE64` | `publish_client.yml` | Optional (iOS signing) | Base64 of the **App Store** provisioning profile for the Network Extension (`com.vpnhood.client.ios.networkextension`). The extension needs its own profile. |
| `APPSTORE_CONNECT_API_KEY` (+ `_API_KEY_ID` + `APPSTORE_CONNECT_ISSUER_ID`) | `publish_client.yml` | Optional (App Store upload) | The App Store Connect API key: the `.p8` **contents**, its Key ID, and the Issuer ID. Present → the `.ipa` is uploaded to TestFlight (prerelease) / App Store (stable). Absent → the upload is skipped with a warning (job stays green). |

## Building your own app (fork-friendly)

The publish scripts are written so a fork can build and release its **own** app without editing any
committed file. Two things are configurable:

**1. Release repository (where artifacts are published and what the generated `*.json` update URLs
point to).** Resolved in this order:

1. `VH_PUBLISH_REPO` env/secret — `owner/repo` (Connect can be split off with `VH_CONNECT_PUBLISH_REPO`;
   unset = same repo as the client).
2. `GITHUB_REPOSITORY` — automatically set in GitHub Actions, so a fork's CI publishes **to itself**.
3. The clone's `origin` remote (local desktop builds).
4. If nothing resolves, an obvious placeholder (`https://your-company-domain/your-product`) so the
   build still succeeds and the unconfigured URL is visible in the generated JSON.

**2. Per-app identity (optional, never committed).** All **non-secret** build settings live in one
`publish.json` at the app root — easy to manage and mirrored by a single GitHub **variable**. The app's
runtime `appsettings.json` is a single **shared** file at the app root, embedded (as `AppSettings.json`)
by every distribution (google, web, windows, linux, and future iOS) — a superset where each distribution
reads the keys it needs and ignores the rest. Signing keys/passwords live in a per-store subfolder
(`google/`, `web/`), one file per GitHub **secret**, with the store in the filename so it matches the
secret name (`android_keystore_google.p12` ↔ `ANDROID_KEYSTORE_GOOGLE_BASE64`):

```
.user/VpnHoodClient/publish.json                            all non-secret config (below)
.user/VpnHoodClient/appsettings.json                        shared app settings (all distributions), embedded
.user/VpnHoodClient/appsettings.Debug.json                  Debug-config override (optional)
.user/VpnHoodClient/google/android_keystore_google.p12      signing key   — secret
.user/VpnHoodClient/google/android_keystore_google_password.txt  store password — secret
.user/VpnHoodConnect/google/access_key_default_google.txt   Connect default access key
.user/VpnHoodConnect/google/access_key_default_google.Debug.txt  Debug-config override (optional)
.user/VpnHoodClient/web/… , .user/VpnHoodConnect/web/…       (per-store signing keys only)
```

`publish.json` (every field optional; absent file/field = project default):

```jsonc
{
  "RepoUrl": "https://github.com/owner/repo",          // release repo for this app (else auto-resolved)
  "PackageTitle": "VpnHoodClient",                     // renames published artifacts only (Android/Windows)
  "InstallationPageUrl": "https://.../download",       // Windows install page; else repo releases/latest
  "Distributions": {
    "Google": { "PackageId": "com.x.android",     "KeystoreAlias": "" },  // google = Play AAB
    "Web":    { "PackageId": "com.x.android.web", "KeystoreAlias": "" }   // web = web + arm64 APKs
  }
}
```

- `PackageId` — the built application id (`/p:ApplicationId`); per store. Absent = the csproj
  `<ApplicationId>` (a `.debug` placeholder), so a fork must set its own to publish a real app.
  Windows/Linux builds have no packageId.
- `KeystoreAlias` — the signing alias (non-secret, hence in the config); absent = auto-detect the
  single key entry, or the optional `ANDROID_KEYSTORE_<NAME>_ALIAS` secret for a multi-entry keystore.
- `PackageTitle` — Linux artifact names come from the csproj `AssemblyName`, so the title does not apply
  there. Most forks leave it at the default.

Any absent file/field keeps the project default, so an unmodified clone builds exactly as before.
(`.user` lives outside the repo and is never committed; create these files when you want to override.)

## Per-platform setup

### Linux client — `publish_app.yml` (via `publish_client.yml`)
No secrets required. Builds self-contained `linux-x64` / `linux-arm64` packages.

### Android client — build (`publish_app.yml`, via `publish_client.yml`)
Builds the Google AAB, the Web APK, and the Web arm64 APK on an `ubuntu-latest` runner,
reusing the existing publish scripts. A JDK 17 and the `.NET` Android workload are set up
on the runner, and the Android SDK is auto-provisioned.

**Signing (optional):** signing config is built by `pub/lib/Initialize-CiAndroidSigning.ps1`.

- Each keystore below is independent: set a key's group and its real keystore is used.
- If a key's secrets are absent, an **ephemeral throwaway keystore** is generated so the build still
  completes — but those artifacts are **not** release/Play-Store grade. Never upload the
  production app-signing key to a public/test repo just to make a test build pass.

To sign with real keys (encode the keystore first: `base64 -w0 my.keystore`):

- `ANDROID_KEYSTORE_GOOGLE_BASE64` / `ANDROID_KEYSTORE_GOOGLE_PASSWORD`
  — the keystore that signs the **Client Google** AAB.
- `ANDROID_KEYSTORE_WEB_BASE64` / `ANDROID_KEYSTORE_WEB_PASSWORD`
  — the keystore that signs the **Client Web** and **Web-arm64** APKs.

Key aliases are **auto-detected** from each keystore at publish time, so you can use your own
keystore without matching our alias or editing the repo. Only if your keystore holds **more than one
key entry** (auto-detect won't guess) set the optional `ANDROID_KEYSTORE_<NAME>_ALIAS` secret naming
the key to use — e.g. `ANDROID_KEYSTORE_GOOGLE_ALIAS`.

Connect publishing, when wired into CI, uses `ANDROID_KEYSTORE_CONNECT_GOOGLE_BASE64` / `_PASSWORD` and
`ANDROID_KEYSTORE_CONNECT_WEB_BASE64` / `_PASSWORD` — each with an optional `_ALIAS` — the same way. They
are separate secrets even though you may load the **same** keystore bytes into both (Connect signs its
Google and Web builds with one key); providing them separately keeps each store's keystore self-contained.
`Initialize-CiAndroidSigning.ps1` materializes each into `.user/<app>/<store>/android_keystore_<store>.p12`
(+ `_password.txt`, optional `_alias.txt`) — see `pub/lib/android-signing.json` for the secret→app/store map.

> The Android client projects currently have AOT disabled (grep `TEMP-CI-AOT-OFF`) to keep
> CI builds fast. Re-enable it before shipping a production release.

### Android client — Google Play (`publish_client.yml`, listing: `publish_metadata_googleplay.yml`)
- `GOOGLE_PLAY_API_KEY`: create a service account in the Google Play Console with the
  *Release* permission, generate a JSON key, and store the file contents.
- Update `fastlane/Appfile` (`package_name`) to **your** application ID — the current
  value `com.vpnhood.client.android` belongs to the upstream project and you cannot
  publish to it.
- Track mapping is automatic: prereleases → `alpha`, stable → `production`.

### Windows client — `publish_app.yml` (via `publish_client.yml`)
The MSI is built with **Advanced Installer** on a `windows-latest` runner.

- **`ADVANCED_INSTALLER_LICENSE`** — your Advanced Installer license ID. The Caphyon
  action installs and registers Advanced Installer with it.

**Code signing (optional, `publish_client.yml`):** signing is **off unless the Azure
credential and the Trusted Signing target are both present**, in which case the build signs
the executable and the MSI via Microsoft Trusted Signing (`sign` CLI) and a signing failure is
fatal. When `AZURE_SIGNING_CREDENTIAL` is absent the MSI is built **unsigned** with a warning. The
`.aip` files themselves carry no signer. To enable it under **your own organization's identity**:

- `AZURE_SIGNING_CREDENTIAL` — the single JSON credential you download from Azure for a service
  principal with the *Trusted Signing Certificate Profile Signer* role, scoped to your signing
  account. Paste the whole file as the secret value (it carries `AZURE_TENANT_ID`,
  `AZURE_CLIENT_ID`, and `AZURE_CLIENT_SECRET`; any extra fields like `subscriptionId` are ignored).
  Locally, the same file is read from `.user/azure_signing_credential.json`.
- `AZURE_SIGNING_TARGET` — a single JSON in Azure Trusted Signing's own `metadata.json` schema
  (the file `signtool`'s dlib consumes), not secret:
  - `CodeSigningAccountName` — your Trusted Signing account name.
  - `CertificateProfileName` — the certificate profile to sign with.
  - `Endpoint` — the regional endpoint, e.g. `https://wus2.codesigning.azure.net/`.

  ```json
  { "Endpoint": "https://wus2.codesigning.azure.net/", "CodeSigningAccountName": "…", "CertificateProfileName": "…" }
  ```

  Locally, the same file is read from `.user/azure_signing_target.json`.

Do not reuse a third-party/previous signer — the published identity comes from the
certificate profile, so verify it resolves to **your** organization before shipping.

### iOS client
`publish_client.yml` has a `build-ios` → `publish-appstore-ios` pair (mirroring Android → Play). Like
every store leg it is **skip-with-warning** when its secrets are absent, but note two hard prerequisites:

- **Runner.** The project targets `net11.0-ios` and needs the .NET 11 SDK + `ios` workload and **Xcode
  26.5+**. GitHub's hosted **`macos-26`** image ships Xcode 26.5 + 26.6, and the job installs .NET 11
  itself — so both iOS jobs run on that hosted image (`runs-on: macos-26`); no self-hosted mac required.
- **Signing can't self-sign.** An App Store `.ipa` requires an **Apple Distribution** certificate and
  **App Store** provisioning profiles issued by Apple — there is no ephemeral fallback. Without them the
  build is unsigned (no `.ipa`) and the upload is skipped.

Secrets: `APPLE_DISTRIBUTION_CERT_BASE64` + `_PASSWORD`, `IOS_PROVISION_APP_BASE64`,
`IOS_PROVISION_EXT_BASE64` (build/signing) and `APPSTORE_CONNECT_API_KEY` + `_API_KEY_ID` +
`APPSTORE_CONNECT_ISSUER_ID` (upload). How to obtain and base64 each is documented step-by-step in
`.user/VpnHoodClient/ios/README.md`. `pub/lib/Initialize-CiIosSigning.ps1` materializes the cert/profiles
into a keychain at build time; `pub/lib/Publish-IosApp.ps1` produces the `.ipa` + `VpnHoodClient-ios.json`.

### Server (separate repo — `vpnhood/VpnHood.App.Server`)

The server releases from a **separate repo**, the same split as Connect: the server's code + version
live in this monorepo, but the release (GitHub release + Docker image) is produced by
`server_publish.yml` **in `vpnhood/VpnHood.App.Server`**, which checks out this monorepo at build time.
That repo has only a `main` branch (no `develop` — there is no code there); the `develop → main`
prerelease/stable model lives here in the monorepo via `bump.yml`. Trigger it with
`pub/Server/PublishByGithub.ps1` (bumps this monorepo, then dispatches the server workflow).

Because the workflow runs **inside** the server repo, it creates the release with the automatic
`github.token` — **no cross-repo PAT needed** (same trick as Connect). One ubuntu job builds both the
Linux packages and the Windows-x64 zip (the server has no MSI/Advanced-Installer step, so Windows
cross-builds on Linux). Secrets/variables live on **that** repo, not here:

| Secret / Variable | Required? | What it is |
|---|---|---|
| `GITHUB_TOKEN` | automatic | Creates the release in the server repo. No setup. |
| `DOCKERHUB_USERNAME` | Optional (Docker push) | Docker Hub account/org that owns the image. Absent → the multi-arch image push is skipped with a warning (the release still ships Linux/Windows + compose files). |
| `DOCKERHUB_TOKEN` | Optional (Docker push) | Docker Hub **access token** (not the password), with Read/Write/Delete on the image repo. |
| `DOCKER_IMAGE` (variable) | Optional | Docker Hub image name (default `vpnhood/vpnhoodserver`). Set for a fork. |
| `CODE_REPO` (variable) | Optional | Monorepo to build the server from (default `vpnhood/VpnHood`). Set for a fork. |

Locally, `pub/Server/Publish.ps1` is **build-only** for smoke tests (add `-docker 1` for a local
host-arch image via `buildx --load`); it never pushes and never creates a release — distribution is
CI-only, like the client.

## Maintainers: keep this in sync
When you add or remove a `secrets.*` reference in any workflow under
`.github/workflows/`, update the table and the relevant section above so forkers
always have an accurate, complete list. The **server** workflow lives in the separate
`vpnhood/VpnHood.App.Server` repo; keep its secrets documented in the Server section above.
