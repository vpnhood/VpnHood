# Tool Repos — executable .NET tools runbook

How to ship a vpnhood **executable** as a .NET tool on NuGet, in its own repo, on its own version
line. Companion to [MODULE-REPOS.md](MODULE-REPOS.md) (libraries that stay version-aligned with the
monorepo) and [RELEASE-STRATEGY.md](RELEASE-STRATEGY.md).

Reference implementation: **`VpnHood.Tools.ResourceTranslator`**. Copy its shape.

## Tool vs module

|  | Module repo | Tool repo |
|---|---|---|
| Ships | a library (`lib/`) | an executable (`tools/`) |
| Consumed with | `dotnet add package` | `dotnet tool install` |
| Version line | aligned with the monorepo (`8.0.x`) | **independent** (`1.x`) |
| Publish credential | org `NUGET_API_KEY` | Trusted Publishing (OIDC) |

A tool is not part of the VPN product's release train — nobody's app breaks when it bumps — so it
keeps its own version and does not adopt the monorepo's. Everything else reuses the module
machinery, including the shared publish script.

## Naming

- **Repo and package id:** `VpnHood.Tools.<Name>` — e.g. `VpnHood.Tools.ResourceTranslator`.
  `Core`/`AppLib`/`Apps` are runtime families; `Tools` is build-time/developer tooling.
- **Command:** short and typed often — `vhtranslator`, `nettester`. Set via `ToolCommandName`; it is
  deliberately *not* the package id.
- **Root namespace:** matches the package id.
- **NuGet package ids are permanent.** You can deprecate but never rename or reuse one. Settle the
  name **before the first publish**; afterwards it is an abandoned id.

## Layout

```text
src/VpnHood.Tools.<Name>/           the tool
tests/VpnHood.Tools.<Name>.Tests/   tests (IsPackable=false)
pub/PubVersion.json                 version state, stamped by CI
.github/workflows/build.yml         CI on push/PR
.github/workflows/publish_nugets.yml  dispatch-triggered publish
_publish.ps1                        local one-shot publish trigger
Directory.Build.props               shared settings + the single <Version>
Directory.Packages.props            central package versions
global.json                         test runner opt-in
CLAUDE.md                           repo guidance
```

## Onboarding checklist

**1. `Directory.Build.props`** — the version element must be **first in the file, in its own leading
PropertyGroup**. The stamper regex-replaces the first match only, so anything version-shaped above it
gets rewritten instead — including a literal version element written inside a comment. Never put one
in a csproj: it overrides this and silently ships a stale number.

```xml
<Project>
	<PropertyGroup>
		<Version>1.0.0</Version>
	</PropertyGroup>
	...
</Project>
```

Also set here: `TargetFramework`, `Nullable`, package metadata (`Authors`, `Company`, `Copyright`,
`PackageProjectUrl`, `RepositoryUrl`, `PackageLicenseExpression`), and SourceLink.

**2. `Directory.Packages.props`** — central package management (`ManagePackageVersionsCentrally`).
Do not put `Version=` on a `PackageReference`. **Exception:** test packages come from the
`MSTest.Sdk` version pinned in the test project's `Sdk` attribute; MSTest.Sdk supplies its own
versions and wins over CPM, so listing them centrally is silently ineffective.

**3. Make it a tool** — three properties in the tool csproj:

```xml
<OutputType>Exe</OutputType>
<PackAsTool>true</PackAsTool>
<ToolCommandName>vhtranslator</ToolCommandName>
```

`PackAsTool` does the rest: `packageType=DotnetTool`, output plus every dependency under `tools/`,
and a generated `DotnetToolSettings.xml`. The package has **no `lib/`** — it cannot be referenced
from code.

**4. `<IsPackable>false</IsPackable>` on every non-shipping project** — tests, samples, benchmarks.
Packing is opt-**out**. Write it plain: the discovery filter is a regex over the raw file text
(`<IsPackable>\s*false\s*</IsPackable>`), so an attribute or a `Condition` will not match and the
project gets published.

**5. `global.json`** — if the tests use Microsoft.Testing.Platform (MSTest.Sdk 4+):

```json
{ "test": { "runner": "Microsoft.Testing.Platform" } }
```

The .NET 10 SDK dropped the VSTest bridge; without this `dotnet test` fails with an error that
mentions VSTest and never mentions the missing opt-in.

**6. `pub/PubVersion.json`** — seed at the current version (or `1.0.0` for a new tool):

```json
{ "Version": "1.0.0", "BumpTime": "2026-07-20T23:07:06.6150390Z" }
```

**7. `.github/workflows/publish_nugets.yml`** — see [Publishing](#publishing) below.

**8. `.github/workflows/build.yml`** — build, test and **pack** on every push and PR. The pack is a
smoke test that catches packaging breakage (license expression, README, tool manifest) *before* a
release; don't upload the artifact, nothing consumes it. Keep this even though publish also builds:
Linux-only failures cannot be reproduced on a Windows dev box, and without CI on `main` the first
signal is a failed release.

**9. `_publish.ps1`** — copy `VpnHood.Tools.ResourceTranslator/_publish.ps1`: refuse a dirty tree →
`git pull` → `git push` → `gh workflow run publish_nugets.yml`. CI does the real work.

**10. `CLAUDE.md` and `README.md`** — README is the NuGet landing page, so lead with install and
usage, not with how to build it. Put layout, architecture and release process in CLAUDE.md.

## Versioning

Tools use `-independentVersion`: the monorepo version is never read and the tool always self-bumps
its own build number. CI owns the bump — it happens **only** on a publish dispatch, before packing,
and is committed back to the branch as `Publish vX.Y.Z`.

Only the **build number** self-bumps. A minor or major is a deliberate hand edit of
`pub/PubVersion.json` **and** `Directory.Build.props` in the same commit.

A failed pack burns a version number on purpose — an unrecorded bump would make the next run
silently `--skip-duplicate` into a no-op. Gaps in the sequence are expected.

## Publishing

Tools publish with [nuget.org Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
— OIDC, no long-lived key.

> **A tool repo cannot use the `publish_module_nugets.yml` reusable workflow.** The OIDC token's
> `job_workflow_ref` identifies the workflow that *requests* the token. Requesting it inside the
> reusable workflow makes nuget.org see `vpnhood/VpnHood` and reject the exchange with
> `401: No matching trust policy`. The `NuGet/login` step must live in the tool repo — and a `uses:`
> job cannot have steps. So the tool repo owns its steps and calls the **shared script** directly,
> exactly like the generated-payload variant in MODULE-REPOS.md.

Create the policy on nuget.org (username → Trusted Publishing):

| Field | Value |
|---|---|
| Policy owner | the **`vpnhood` organization** |
| Repository Owner | `vpnhood` |
| Repository | `VpnHood.Tools.<Name>` |
| Workflow File | `publish_nugets.yml` — **file name only** |
| Environment | empty |

The policy binds to that **file name**; renaming the workflow breaks publishing until the policy is
updated. The failure is explicit — `Workflow mismatch for policy ...: expected X, actual Y`.

Then the workflow:

```yaml
permissions:
  contents: write   # the shared script pushes the bump commit back
  id-token: write   # OIDC token for the exchange

jobs:
  publish:
    if: github.repository_owner == 'vpnhood'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with: { path: module, persist-credentials: true }   # never the workspace root
      - uses: actions/checkout@v7
        with: { repository: vpnhood/VpnHood, ref: develop, path: vh, sparse-checkout: pub }
      - uses: actions/setup-dotnet@v6
        with: { dotnet-version: "10.0.x" }
      - run: |
          git -C module config user.name "github-actions[bot]"
          git -C module config user.email "41898282+github-actions[bot]@users.noreply.github.com"
      - uses: NuGet/login@v1            # must run in THIS repo
        id: nuget-login
        with: { user: "${{ secrets.NUGET_USER }}" }
      - shell: pwsh
        env:
          NUGET_API_KEY: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}
        run: |
          & "$env:GITHUB_WORKSPACE/vh/pub/lib/Publish-ModuleNugetPackages.ps1" `
            -moduleDir "$env:GITHUB_WORKSPACE/module" `
            -branch "${{ github.ref_name }}" `
            -independentVersion `
            -prerelease:$("${{ inputs.prerelease }}" -eq "true")
```

Requirements:

- **`NUGET_USER` repository secret** — the nuget.org *profile name*, not an email. It is the only
  secret a tool repo needs.
- **Keep the tool checkout under `module/`.** Packable-project discovery is a recursive `*.csproj`
  glob; a checkout at the workspace root would pack the monorepo too.
- **Request the key immediately before publishing.** It is valid one hour and single-use.
- **The repo must live under the `vpnhood` org** — the job is gated `if: github.repository_owner ==
  'vpnhood'`, and outside the org it is skipped silently with a green run.
- **`@develop` is a mutable pin.** A monorepo change can break your publish without warning; that is
  the accepted trade for internal lockstep.

## Local dry run

```powershell
pwsh VpnHood/pub/lib/Publish-ModuleNugetPackages.ps1 -moduleDir ../VpnHood.Tools.MyTool `
  -independentVersion -noPush
```

Stamps the version files and packs into `pub/bin/nuget` without committing, pushing or publishing.
It **does** rewrite your local `PubVersion.json` and `Directory.Build.props` — revert them
afterwards, or CI's bump will skip a number. Add `/pub/bin/` to `.gitignore`.

## Gotchas learned the hard way

- **Culture validation differs by platform.** `CultureInfo.GetCultureInfo("Anything")` throws on
  Windows (NLS) but *fabricates* a culture on Linux/macOS (ICU). Code that treats "did it throw?" as
  "is this a real culture?" passes locally and fails in CI. Match against
  `CultureInfo.GetCultures(CultureTypes.AllCultures)` instead. More generally: CI is Linux, dev boxes
  are Windows — a green local run is not a green CI run.
- **MSTest 4 removed `Assert.ThrowsException`.** Use `Assert.ThrowsExactly` /
  `ThrowsExactlyAsync`.
- **Action majors move.** Node 20 is deprecated on runners; use `actions/checkout@v7`,
  `actions/setup-dotnet@v6`, `actions/upload-artifact@v7` or newer.
- **`dotnet.config` is not the MTP opt-in** — `global.json` is (`test.runner`). The error message
  points at neither.
- **Symbols ride along.** Pushing a `.nupkg` also pushes its adjacent `.snupkg`; an explicit
  `**/*.nupkg` glob does not match `.snupkg`.
- **Fill in `LICENSE` placeholders** before publishing — the file ships inside the package. Tools use
  LGPL-2.1-only, same as the rest of the family.
