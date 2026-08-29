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
| `PUBLISHER_APP_PRIVATE_KEY` (+ `PUBLISHER_APP_ID` **Variable**) | `publish_app.yml` (release job) | Required only for a **cross-repo release** | Private key + App ID of a GitHub App installed on the release repo with `Contents: Read and write`. Needed only when `VH_PUBLISH_REPO` points somewhere other than the repo running the workflow — `github.token` is scoped to the caller and cannot write a release elsewhere. Set on the **publishing** repo (e.g. `Vpnhood.App.Client`), not on the release repo. Absent while a cross-repo release is requested → the run fails loudly rather than 404-ing inside `gh`. |
| `GOOGLE_PLAY_API_KEY` | `publish_client.yml` (in `Vpnhood.App.Client`), `publish_metadata_googleplay.yml` | Optional (Play) | Google Play service-account JSON (whole file contents). Present → `publish_client.yml` (in `Vpnhood.App.Client`) publishes the AAB to Play and attaches the Play-signed APK to the release. Absent → the Play publish is skipped with a warning (the job stays green); nothing is pushed to Google Play. |
| `ADVANCED_INSTALLER_LICENSE` | `publish_client.yml` (in `Vpnhood.App.Client`) | Required for Windows | Advanced Installer license ID (used to register AI on the runner). |
| `AZURE_SIGNING_CREDENTIAL` | `publish_client.yml` (in `Vpnhood.App.Client`) | Optional (Windows signing) | The single Azure service-principal JSON you download from Azure (contains `AZURE_TENANT_ID`/`AZURE_CLIENT_ID`/`AZURE_CLIENT_SECRET`; other fields ignored). Paste the whole file. Absent **together with** `AZURE_SIGNING_TARGET` → MSI builds unsigned with a warning; present without it → the build **fails** (see the pair rule below). |
| `AZURE_SIGNING_TARGET` | `publish_client.yml` (in `Vpnhood.App.Client`) | Optional (Windows signing) | Single JSON in Azure Trusted Signing's `metadata.json` schema: `Endpoint`, `CodeSigningAccountName`, `CertificateProfileName`. Not secret and not part of the Azure credential file; required alongside it for signing to run. Store it as an org/repository **Variable**. These two are an all-or-nothing **pair**: both set → signed, neither set → unsigned + warning, exactly one set → the build **fails**, because a half-configured signer ships an unsigned installer from a green run. |
| `ANDROID_KEYSTORE_GOOGLE_BASE64` / `_PASSWORD` (+ optional `_ALIAS`) | `publish_client.yml` (in `Vpnhood.App.Client`) | Optional (Android signing) | Base64 of the keystore that signs the Client Google AAB, plus its store password. The key alias is auto-detected; set `_ALIAS` only for a multi-entry keystore. |
| `ANDROID_KEYSTORE_WEB_BASE64` / `_PASSWORD` (+ optional `_ALIAS`) | `publish_client.yml` (in `Vpnhood.App.Client`) | Optional (Android signing) | Base64 of the keystore that signs the Client Web + Web-arm64 APKs, plus its store password. Alias auto-detected; set `_ALIAS` only for a multi-entry keystore. |
| `ANDROID_KEYSTORE_CONNECT_GOOGLE_BASE64` / `_PASSWORD` (+ optional `_ALIAS`) | `connect_publish.yml` (in `Vpnhood.App.Connect`) | Optional (Android signing) | Base64 of the keystore that signs the Connect Google AAB, plus its store password. Alias auto-detected; set `_ALIAS` only for a multi-entry keystore. May reuse the same keystore as Connect Web. |
| `ANDROID_KEYSTORE_CONNECT_WEB_BASE64` / `_PASSWORD` (+ optional `_ALIAS`) | `connect_publish.yml` (in `Vpnhood.App.Connect`) | Optional (Android signing) | Base64 of the keystore that signs the Connect Web APKs, plus its store password. Alias auto-detected; set `_ALIAS` only for a multi-entry keystore. May reuse the same keystore as Connect Google. |
| `APPLE_DISTRIBUTION_CERT_BASE64` / `_PASSWORD` | `publish_client.yml` (in `Vpnhood.App.Client`), `connect_publish.yml` (in `Vpnhood.App.Connect`) | Optional (iOS signing) | Base64 of the Apple **Distribution** certificate `.p12` (with private key) that signs the iOS `.ipa`, plus its export password. ONE cert signs every app of the team — set it as an **organization** secret visible to both app repos (or repeat it per repo). Absent → the iOS build is UNSIGNED (no `.ipa`, a warning); there is no ephemeral fallback (App Store builds can't self-sign). |
| `IOS_PROVISION_APP_BASE64` | `publish_client.yml` (in `Vpnhood.App.Client`), `connect_publish.yml` (in `Vpnhood.App.Connect`) | Optional (iOS signing) | Base64 of the **App Store** provisioning profile for the app. Per-app **repository** secret — the profile is minted for that repo's own bundle id (Client `com.vpnhood.client.ios`, Connect `com.vpnhood.connect.ios`). |
| `IOS_PROVISION_EXT_BASE64` | `publish_client.yml` (in `Vpnhood.App.Client`), `connect_publish.yml` (in `Vpnhood.App.Connect`) | Optional (iOS signing) | Base64 of the **App Store** provisioning profile for that app's Network Extension (`…ios.networkextension`). The extension needs its own profile. |
| `ACCESS_KEY_AD` / `ACCESS_KEY_PREMIUM` | `_build_app_android.yml`, `_build_app_ios.yml`, `_build_app_windows.yml`, `_build_app_linux.yml` | **Required (Connect only)** | The `vh://…` default server access key embedded in each Connect distribution, written to `.user/VpnHoodConnect/access_key_ad.txt` / `access_key_premium.txt` before the build. Keys are per-entitlement, not per-store: `ACCESS_KEY_AD` = the ad-supported Android Google (Play) build only; `ACCESS_KEY_PREMIUM` = every ad-free distribution (iOS **+ Android web APK + Windows MSI + Linux**). Absent on a strict publish → the build **FAILS** (`Assert-DefaultAccessKey`): Connect without a default server opens on an empty server list. Ignored by Client builds, and skipped entirely for a fork with no `PUBLISH` variable. |
| `APPSTORE_CONNECT_API_KEY` (+ `_API_KEY_ID` + `APPSTORE_CONNECT_ISSUER_ID`) | `publish_client.yml` (in `Vpnhood.App.Client`), `connect_publish.yml` (in `Vpnhood.App.Connect`) | Optional (App Store upload) | The App Store Connect API key: the `.p8` **contents** (raw PEM text, NOT base64), its Key ID, and the Issuer ID. Team-wide like the cert — suits an **organization** secret. Present → the `.ipa` is uploaded to TestFlight (prerelease) / App Store (stable). Absent → the upload is skipped with a warning (job stays green). |

## Building your own app (fork-friendly)

The publish scripts are written so a fork can build and release its **own** app without editing any
committed file. Two things are configurable:

**1. Release repository (where artifacts are published and what the generated `*.json` update URLs
point to).** Resolved in this order:

1. `VH_PUBLISH_REPO` env/secret/**variable** — `owner/repo` (Connect can be split off with
   `VH_CONNECT_PUBLISH_REPO`; unset = same repo as the client).
2. `GITHUB_REPOSITORY` — automatically set in GitHub Actions, so a fork's CI publishes **to itself**.
3. The clone's `origin` remote (local desktop builds).
4. If nothing resolves, an obvious placeholder (`https://your-company-domain/your-product`) so the
   build still succeeds and the unconfigured URL is visible in the generated JSON.

In CI, `publish_app.yml` and every `_build_app_*` module read the `VH_PUBLISH_REPO` **repository
variable of the calling repo** and fall back to `GITHUB_REPOSITORY`. Setting it is what lets a
store-asset repo publish an app whose Releases page lives somewhere else — the client is dispatched
from `Vpnhood.App.Client` but releases into `vpnhood/VpnHood`, so existing download links, README
badges and in-app update URLs keep resolving.

Two things must both be true for that split, and each fails loudly if it is not:

- **The builds must see it too, not just the release job.** The generated `*.json` install/update
  URLs are written during the per-platform builds. That is why the variable is read in all four
  `_build_app_*` modules — reading it only at release time would ship JSON pointing at a repo with
  no releases, and nothing in the run would go red.
- **A cross-repo release needs `PUBLISHER_APP_ID` + `PUBLISHER_APP_PRIVATE_KEY`** on the publishing
  repo (see the table above). `github.token` is scoped to the caller and cannot create a release
  elsewhere.

**2. Per-app identity (optional, never committed).** All **non-secret** build settings live in one
`publish.json` at the app root — easy to manage and mirrored by a single GitHub **variable**. The app's
runtime `appsettings.json` is a single **shared** file at the app root, embedded (as `AppSettings.json`)
by every distribution (google, web, windows, linux, ios) — a superset where each distribution
reads the keys it needs and ignores the rest. Signing keys/passwords live in a per-store subfolder
(`google/`, `web/`), one file per GitHub **secret**, with the store in the filename so it matches the
secret name (`android_keystore_google.p12` ↔ `ANDROID_KEYSTORE_GOOGLE_BASE64`):

```
.user/VpnHoodClient/publish.json                            all non-secret config (below)
.user/VpnHoodClient/appsettings.json                        shared app settings (all distributions), embedded
.user/VpnHoodClient/appsettings.Debug.json                  Debug-config override (optional)
.user/VpnHoodClient/google/android_keystore_google.p12      signing key   — secret
.user/VpnHoodClient/google/android_keystore_google_password.txt  store password — secret
.user/VpnHoodConnect/access_key_ad.txt                      Connect default access key, ad-supported build (Android Google only)
.user/VpnHoodConnect/access_key_ad.Debug.txt                Debug-config override (optional)
.user/VpnHoodConnect/access_key_premium.txt                 same, for EVERY ad-free distribution (iOS, Android web APK, Windows MSI, Linux)
.user/VpnHoodConnect/access_key_premium.Debug.txt           Debug-config override (optional)
.user/VpnHoodClient/web/… , .user/VpnHoodConnect/web/…       (per-store signing keys only)
.user/<app>/ios/ios_provision_app.mobileprovision           App Store profile, app        — secret IOS_PROVISION_APP_BASE64
.user/<app>/ios/ios_provision_ext.mobileprovision           App Store profile, extension  — secret IOS_PROVISION_EXT_BASE64
.user/<app>/ios/ios_signing.json                            local marker ({ "Signed": false } = build unsigned); CI regenerates it from the secrets
.user/apple_distribution_cert.p12 (+ _password.txt)         ROOT, not per-app: one Apple Distribution cert signs every app — org secret APPLE_DISTRIBUTION_CERT_BASE64/_PASSWORD
.user/appstore_connect_api_key_<KEYID>.p8 (+ id/issuer txt) ROOT: App Store Connect API key — org secrets APPSTORE_CONNECT_API_KEY/_API_KEY_ID/_ISSUER_ID
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

### Linux client — `publish_app.yml` (via `publish_client.yml` (in `Vpnhood.App.Client`))
No secrets required. Builds self-contained `linux-x64` / `linux-arm64` packages.
**Connect** additionally requires `ACCESS_KEY_PREMIUM` (Linux has no ads, so it embeds the 'premium'
key shared by every ad-free distribution); the build fails without it on a strict publish.

### Android client — build (`publish_app.yml`, via `publish_client.yml` (in `Vpnhood.App.Client`))
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

### Android client — Google Play (`publish_client.yml` (in `Vpnhood.App.Client`); the store LISTING ships separately via `publish_listing.yml`)
- `GOOGLE_PLAY_API_KEY`: create a service account in the Google Play Console with the
  *Release* permission, generate a JSON key, and store the file contents.
- Update `fastlane/Appfile` (`package_name`) to **your** application ID — the current
  value `com.vpnhood.client.android` belongs to the upstream project and you cannot
  publish to it.
- Track mapping is automatic: prereleases → `alpha`, stable → `production`.

### Store listings — `publish_listing.yml` (reusable; dispatched from the store-asset repos)

Listing text + screenshots (Google Play and the App Store) do **not** ship with releases: the
store-asset repos (`Vpnhood.App.Client` / `Vpnhood.App.Connect`) dispatch their `publish_listing.yml`
stub, which calls this repo's reusable workflow. It runs in the caller's context, so the caller repo
needs the credentials (`GOOGLE_PLAY_API_KEY`, `APPSTORE_CONNECT_*`) and gets the published-state
commit — no PAT anywhere. The full maintainer's map for the listing pipeline (tools, invariants,
verification commands, Apple failure lore) lives in
`VpnHood.Client.WebUI/e2e/store/README.md` — read it before changing any of these workflows.

### Windows client — `publish_app.yml` (via `publish_client.yml` (in `Vpnhood.App.Client`))
The MSI is built with **Advanced Installer** on a `windows-latest` runner.

- **`ADVANCED_INSTALLER_LICENSE`** — your Advanced Installer license ID. The Caphyon
  action installs and registers Advanced Installer with it.
- **`ACCESS_KEY_PREMIUM`** (Connect only) — the default server key baked into the MSI. Windows has no
  ads, so it embeds the 'premium' key shared by every ad-free distribution. The build fails without it
  on a strict publish, so an installer can never ship with an empty server list.

**Code signing (optional, `publish_client.yml` (in `Vpnhood.App.Client`)):** signing is **off unless the Azure
credential and the Trusted Signing target are both present**, in which case the build signs
every published binary that does not already carry a vendor signature (the apphost `.exe` plus our
own `VpnHood*.dll` assemblies; the .NET runtime, WinDivert and Advanced Installer's `updater.exe`
keep their Microsoft/vendor signatures) and then the MSI, via Azure Trusted Signing — now surfaced as
**Artifact Signing** (`sign code artifact-signing`; the old `trusted-signing` verb still works but the
CLI marks it obsolete). Any file that already carries a signature is never re-signed, and a signing
failure is fatal. The same pair (via `SignFiles.ps1`) is honoured by `publish_nugets.yml`, which
signs each package's own assembly between build and pack. The `.nupkg` wrapper is deliberately left
to nuget.org's own repository signature, applied at ingestion. Do NOT add author signing: it requires
registering a long-lived certificate on the nuget.org account, after which *every* future push must
be signed with it — impossible with Trusted Signing's ~3-day rotating certificates, so the whole
suite would stop publishing at the next rotation (NuGetGallery#10027). They are an all-or-nothing **pair**: with **neither** set the MSI is built **unsigned** with a
warning (the fork-friendly path), but with **exactly one** set the build **fails** — a half-configured
signer otherwise ships an unsigned installer from a green run, which is how every release before
8.1.843 went out unsigned unnoticed (the org had the credential; the target was never set anywhere).
The `.aip` files themselves carry no signer. To enable it under **your own organization's identity**:

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

### iOS (Client and Connect)
`publish_client.yml` (in `Vpnhood.App.Client`) and `connect_publish.yml` (in `Vpnhood.App.Connect`) each drive a
`build-ios` → `publish-appstore-ios` pair (mirroring Android → Play); the secrets resolve from whichever app repo
dispatches the run. Like every store leg it is **skip-with-warning** when its secrets are absent, but note two hard
prerequisites:

- **Runner.** The project targets `net11.0-ios` and needs the .NET 11 SDK + `ios` workload and **Xcode
  26.5+**. GitHub's hosted **`macos-26`** image ships Xcode 26.5 + 26.6, and the job installs .NET 11
  itself — so both iOS jobs run on that hosted image (`runs-on: macos-26`); no self-hosted mac required.
- **Signing can't self-sign.** An App Store `.ipa` requires an **Apple Distribution** certificate and
  **App Store** provisioning profiles issued by Apple — there is no ephemeral fallback. Without them the
  build is unsigned (no `.ipa`) and the upload is skipped.
- **Export compliance is a hard upload gate.** All four iOS plists declare
  `ITSAppUsesNonExemptEncryption=false` — Apple's prescribed value for standards-body-only crypto
  with no French-store distribution ("no documentation needed"); it makes uploads pass with no
  per-build compliance questions. **Never flip it to `true`**: true requires an Apple-issued code
  the non-France flow never grants, and every upload is then rejected with error 90592. Semantics,
  the one-time wizard, the BIS annual report, and the description text Apple asks for are all in
  [docs/legal/APP_STORE_EXPORT_COMPLIANCE.md](../legal/developer/APP_STORE_EXPORT_COMPLIANCE.md).

Secrets: `APPLE_DISTRIBUTION_CERT_BASE64` + `_PASSWORD`, `IOS_PROVISION_APP_BASE64`,
`IOS_PROVISION_EXT_BASE64` (build/signing) and `APPSTORE_CONNECT_API_KEY` + `_API_KEY_ID` +
`APPSTORE_CONNECT_ISSUER_ID` (upload). `pub/lib/Initialize-CiIosSigning.ps1` materializes the cert/profiles
into a CI keychain at build time (writing `.user/<app>/ios/ios_signing.json`); `pub/lib/Publish-IosApp.ps1`
produces the `.ipa` + `<PackageTitle>-ios.json`.

#### Obtaining the Apple-issued iOS assets (fork checklist)

Every asset below must be **issued by Apple** for your own developer account — none can be generated
locally, and without them the build runs unsigned (compile check only, no `.ipa`). Substitute your own
bundle ids throughout; each app needs its **own pair** of App Store profiles (app + Network Extension),
while the certificate and the API key are one-per-team.

| You need | GitHub secret | How to get it |
|---|---|---|
| Apple **Distribution** cert `.p12` (incl. private key) | `APPLE_DISTRIBUTION_CERT_BASE64` | Xcode → Settings → Accounts → Manage Certificates → **+ → Apple Distribution**, then export from Keychain Access as `.p12`. (Or the developer portal: Certificates → **Apple Distribution** from a CSR.) |
| …its export password | `APPLE_DISTRIBUTION_CERT_PASSWORD` | The password you chose when exporting the `.p12`. |
| **App Store** provisioning profile for the app | `IOS_PROVISION_APP_BASE64` | Developer portal → Profiles → **+ → App Store** → the app's App ID → the Distribution cert. Download the `.mobileprovision`. |
| **App Store** provisioning profile for the extension | `IOS_PROVISION_EXT_BASE64` | Same, for the `…networkextension` App ID. The extension needs its **own** profile. |
| App Store Connect API key `.p8` contents | `APPSTORE_CONNECT_API_KEY` | App Store Connect → Users and Access → **Integrations → App Store Connect API** → generate a key (App Manager role). Download the `AuthKey_XXXX.p8` — Apple offers it **once**. |
| …the key's Key ID | `APPSTORE_CONNECT_API_KEY_ID` | Shown next to the key. |
| …the Issuer ID | `APPSTORE_CONNECT_ISSUER_ID` | Shown at the top of the API keys page. |

Turn the files into secrets and set them on the **app repo** that dispatches the publish (the cert and
API key rows suit organization secrets when several app repos share them):

```bash
base64 -i AppleDistribution.p12 | gh secret set APPLE_DISTRIBUTION_CERT_BASE64 -R <owner>/<app-repo>
base64 -i App.mobileprovision   | gh secret set IOS_PROVISION_APP_BASE64      -R <owner>/<app-repo>
base64 -i Ext.mobileprovision   | gh secret set IOS_PROVISION_EXT_BASE64      -R <owner>/<app-repo>
gh secret set APPSTORE_CONNECT_API_KEY -R <owner>/<app-repo> < AuthKey_XXXX.p8   # raw .p8 text, NOT base64
```

Two Apple gotchas that cost real time (details in
[docs/ios/build-deploy-and-provisioning.md](../ios/build-deploy-and-provisioning.md), which also
carries the full white-label checklist for in-app purchase, Sign in with Apple and App Store Server
Notifications):

- **Changing capabilities on an App ID invalidates every existing profile on it.** Regenerate + re-download
  the profiles and refresh the `IOS_PROVISION_*` secrets afterwards — Apple never retrofits a profile.
- **Renaming a profile in the portal regenerates it** (new UUID) — re-download and refresh the secret.

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
