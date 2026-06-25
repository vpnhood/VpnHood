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
| `ADVINST_LICENSE` | `client_windows_build.yml` | Required for Windows | Advanced Installer license ID (used to register AI on the runner). |

## Per-platform setup

### Linux client — `client_linux_build.yml`
No secrets required. Builds self-contained `linux-x64` / `linux-arm64` packages.

### Android client — Google Play (`publish_store_*.yml`)
- `PLAY_JSON_KEY`: create a service account in the Google Play Console with the
  *Release* permission, generate a JSON key, and store the file contents.
- Update `fastlane/Appfile` (`package_name`) to **your** application ID — the current
  value `com.vpnhood.client.android` belongs to the upstream project and you cannot
  publish to it.
- Track mapping is automatic: prereleases → `alpha`, stable → `production`.

### Windows client — `client_windows_build.yml`
The MSI is built with **Advanced Installer** on a `windows-latest` runner.

- **`ADVINST_LICENSE`** — your Advanced Installer license ID. The Caphyon action
  installs and registers Advanced Installer with it.

**Code signing:** the MSI and its executables are currently built **unsigned** — the
`.aip` files carry no signer. To ship signed builds, configure a signing certificate
under **your own organization's identity** in the `.aip` (e.g. Azure Trusted Signing or
a PFX) and add the corresponding secrets. Do not reuse a third-party/previous signer.

### iOS client
Not applicable yet — there is no iOS app project in the repo.

## Maintainers: keep this in sync
When you add or remove a `secrets.*` reference in any workflow under
`.github/workflows/`, update the table and the relevant section above so forkers
always have an accurate, complete list.
