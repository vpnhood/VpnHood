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
| `AZURE_TENANT_ID` | `client_windows_build.yml` | Required for signed Windows | Entra tenant ID of the code-signing service principal. |
| `AZURE_CLIENT_ID` | `client_windows_build.yml` | Required for signed Windows | App (client) ID of the signing service principal. |
| `AZURE_CLIENT_SECRET` | `client_windows_build.yml` | Required for signed Windows | Client secret of the signing service principal. |

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
The MSI is built with **Advanced Installer** and the binaries are **code-signed via
Azure Trusted Signing**. You need both an AI license and an Azure signing identity.

1. **`ADVINST_LICENSE`** — your Advanced Installer license ID.
2. **Azure Trusted Signing** — the `.aip` signs against a specific Trusted Signing
   account + certificate profile. Point it at your own, then create a **least-privilege
   service principal** scoped to *only* your certificate profile:
   ```bash
   SCOPE="/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.CodeSigning/codeSigningAccounts/<account>/certificateProfiles/<profile>"
   az ad sp create-for-rbac \
     --name "ci-signing" \
     --role "Artifact Signing Certificate Profile Signer" \
     --scopes "$SCOPE"
   ```
   Store the resulting `tenant` / `appId` / `password` as `AZURE_TENANT_ID` /
   `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET`. Advanced Installer picks these up via
   `DefaultAzureCredential` at build time. The client secret expires (default 1 year) —
   rotate it in the app registration's **Certificates & secrets** blade.
   > If you don't have Trusted Signing, edit the `.aip` to disable signing or use a
   > different signing method; then the Azure secrets are not needed.

### iOS client
Not applicable yet — there is no iOS app project in the repo.

## Maintainers: keep this in sync
When you add or remove a `secrets.*` reference in any workflow under
`.github/workflows/`, update the table and the relevant section above so forkers
always have an accurate, complete list.
