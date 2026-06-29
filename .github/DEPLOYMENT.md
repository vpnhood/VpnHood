# Deployment & Required Secrets (for forks)

This repo publishes the VpnHood **client** apps through GitHub Actions. If you fork
the repo and want the publishing workflows to run, you must provide your own
credentials as **repository secrets** — the originals are never committed (they live
outside the repo, in a sibling `../.user/` folder, on the maintainer's machine).

> This document is the source of truth for *what* secrets each workflow needs.
> Keep it in sync whenever a workflow gains or drops a `secrets.*` reference.

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
| `GH_TOKEN` | `publish_store_package.yml` | Optional | A PAT used only if you need broader scope than `GITHUB_TOKEN`; otherwise it falls back to `GITHUB_TOKEN`. |
| `PLAY_JSON_KEY` | `publish_store_package.yml`, `publish_store_contents.yml` | Required for Play | Google Play service-account JSON (whole file contents). |
| `ADVANCED_INSTALLER_LICENSE` | `client_windows_build.yml`, `client_publish.yml` | Required for Windows | Advanced Installer license ID (used to register AI on the runner). |
| `AZURE_TENANT_ID` / `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET` | `client_publish.yml` | Optional (Windows signing) | Service principal for Azure Trusted Signing. All three (plus the `VH_SIGN_*` set) must be present for signing to run. |
| `VH_SIGN_ACCOUNT` / `VH_SIGN_PROFILE` / `VH_SIGN_ENDPOINT` | `client_publish.yml` | Optional (Windows signing) | Trusted Signing target: account name, certificate profile, and regional endpoint. |
| `ANDROID_KEYSTORE_CLIENT_GOOGLE_BASE64` / `_PASS` (+ optional `_ALIAS`) | `client_android_build.yml`, `client_publish.yml` | Optional (Android signing) | Base64 of the keystore that signs the Client Google AAB, plus its store password. The key alias is auto-detected; set `_ALIAS` only for a multi-entry keystore. |
| `ANDROID_KEYSTORE_CLIENT_WEB_BASE64` / `_PASS` (+ optional `_ALIAS`) | `client_android_build.yml`, `client_publish.yml` | Optional (Android signing) | Base64 of the keystore that signs the Client Web + Web-arm64 APKs, plus its store password. Alias auto-detected; set `_ALIAS` only for a multi-entry keystore. |

## Per-platform setup

### Linux client — `client_linux_build.yml`
No secrets required. Builds self-contained `linux-x64` / `linux-arm64` packages.

### Android client — build (`client_android_build.yml`, `client_publish.yml`)
Builds the Google AAB, the Web APK, and the Web arm64 APK on an `ubuntu-latest` runner,
reusing the existing publish scripts. A JDK 17 and the `.NET` Android workload are set up
on the runner, and the Android SDK is auto-provisioned.

**Signing (optional):** signing config is built by `Pub/Core/PrepareCiAndroidSigning.ps1`.

- Each keystore below is independent: set a key's group and its real keystore is used.
- If a key's secrets are absent, an **ephemeral throwaway keystore** is generated so the build still
  completes — but those artifacts are **not** release/Play-Store grade. Never upload the
  production app-signing key to a public/test repo just to make a test build pass.

To sign with real keys (encode the keystore first: `base64 -w0 my.keystore`):

- `ANDROID_KEYSTORE_CLIENT_GOOGLE_BASE64` / `ANDROID_KEYSTORE_CLIENT_GOOGLE_PASS`
  — the keystore that signs the **Client Google** AAB.
- `ANDROID_KEYSTORE_CLIENT_WEB_BASE64` / `ANDROID_KEYSTORE_CLIENT_WEB_PASS`
  — the keystore that signs the **Client Web** and **Web-arm64** APKs.

Key aliases are **auto-detected** from each keystore at publish time, so you can use your own
keystore without matching our alias or editing the repo. Only if your keystore holds **more than one
key entry** (auto-detect won't guess) set the optional `ANDROID_KEYSTORE_<NAME>_ALIAS` secret naming
the key to use — e.g. `ANDROID_KEYSTORE_CLIENT_GOOGLE_ALIAS`. (Connect publishing, when wired into
CI, uses `ANDROID_KEYSTORE_CONNECT_BASE64` / `_PASS` — and optional `_ALIAS` — the same way.)

> The Android client projects currently have AOT disabled (grep `TEMP-CI-AOT-OFF`) to keep
> CI builds fast. Re-enable it before shipping a production release.

### Android client — Google Play (`publish_store_*.yml`)
- `PLAY_JSON_KEY`: create a service account in the Google Play Console with the
  *Release* permission, generate a JSON key, and store the file contents.
- Update `fastlane/Appfile` (`package_name`) to **your** application ID — the current
  value `com.vpnhood.client.android` belongs to the upstream project and you cannot
  publish to it.
- Track mapping is automatic: prereleases → `alpha`, stable → `production`.

### Windows client — `client_windows_build.yml`
The MSI is built with **Advanced Installer** on a `windows-latest` runner.

- **`ADVANCED_INSTALLER_LICENSE`** — your Advanced Installer license ID. The Caphyon
  action installs and registers Advanced Installer with it.

**Code signing (optional, `client_publish.yml`):** signing is **off unless all six
signing secrets are present**, in which case the build signs the executable and the MSI
via Microsoft Trusted Signing (`sign` CLI) and a signing failure is fatal. The `.aip`
files themselves carry no signer. To enable it under **your own organization's identity**:

- `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` — a service principal with
  the *Trusted Signing Certificate Profile Signer* role, scoped to your signing account.
- `VH_SIGN_ACCOUNT` — your Trusted Signing account name.
- `VH_SIGN_PROFILE` — the certificate profile to sign with.
- `VH_SIGN_ENDPOINT` — the regional endpoint, e.g. `https://wus2.codesigning.azure.net/`.

Do not reuse a third-party/previous signer — the published identity comes from the
certificate profile, so verify it resolves to **your** organization before shipping.

### iOS client
Not applicable yet — there is no iOS app project in the repo.

## Maintainers: keep this in sync
When you add or remove a `secrets.*` reference in any workflow under
`.github/workflows/`, update the table and the relevant section above so forkers
always have an accurate, complete list.
