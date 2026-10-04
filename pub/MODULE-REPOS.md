# Module Repos — independent versioning runbook

How to give a vpnhood library its own repo and its own NuGet cadence while keeping it
**version-aligned with the monorepo family**. Companion to [RELEASE-STRATEGY.md](RELEASE-STRATEGY.md)
(which covers the monorepo's own model); this file is the step-by-step for onboarding a new module.

Reference implementation: **`VpnHood.Net.Proxies`**. Copy its shape.

> Shipping an **executable** rather than a library? See [TOOL-REPOS.md](TOOL-REPOS.md) — tools reuse
> this machinery but keep an independent version line.

## When a library earns a module repo

Move a library out of the monorepo only when it has a genuinely independent life:

- outside consumers who don't care about VpnHood's release cadence, or
- a release rhythm that shouldn't drag ~60 other packages along, or
- a payload the monorepo shouldn't carry (large binary assets, an npm toolchain).

If none of those apply, leave it in `src/` — the monorepo's one-version-for-everything model is the
lowest-maintenance option and a module repo costs you cross-repo release ordering.

## The version rule

One line of code in [lib/Publish-ModuleNugetPackages.ps1](lib/Publish-ModuleNugetPackages.ps1) decides
every module version:

```powershell
$version = if ($vhVersion -gt $moduleVersion) { $vhVersion } else { [version]::new($moduleVersion.Major, $moduleVersion.Minor, $moduleVersion.Build + 1) };
```

Read it as: **adopt the monorepo when it's ahead, otherwise self-bump the build number.**

| Monorepo `develop` | Module | Next module version | Why |
|---|---|---|---|
| `8.0.834` | `8.0.834` | `8.0.835` | equal → self-bump |
| `8.0.900` | `8.0.834` | `8.0.900` | monorepo ahead → **adopt verbatim**, no increment |
| `8.0.834` | `8.0.900` | `8.0.901` | module ran ahead → self-bump; monorepo leapfrogs later and re-syncs |

Consequences worth internalising:

- It is **not** literally `max()`. When the monorepo is ahead the module adopts that exact number
  *without* incrementing — so two modules that publish at the same time can land on the same version.
  That's intended: the family shares a version line.
- **Major/Minor never self-bump.** Only `Build` increments locally. A module can only reach `9.0.x`
  by adopting it from the monorepo. If a module needs an independent major, this model is the wrong
  fit — give it a real independent version line with `-independentVersion` (see below).
- The monorepo version is **always read from `develop`**, hardcoded as the script's default
  (`https://raw.githubusercontent.com/vpnhood/VpnHood/develop/pub/PubVersion.json`), deliberately
  independent of the ref the scripts are checked out from — so pinning that checkout can never
  silently freeze the version source. `develop` always carries the highest version; `main` only advances on a stable bump.
- **The branch does not affect the version.** It only decides where the bump commit is pushed
  (`git push origin HEAD:$branch`).
- The bump is **committed before packing**, on purpose: a failed pack burns a cheap version number,
  whereas an unrecorded bump would make the next run silently `--skip-duplicate` into a no-op.

### Opting out: independent version lines

A module that is **not part of the VPN product's release train** — a standalone developer tool rather
than a library the apps consume — should not have its version leap to `8.0.x`. Pass
`-independentVersion` to the script and the monorepo version is never read: the module always self-bumps its own build number. Everything else is
unchanged, including "only `Build` self-bumps", so a minor/major there is still a deliberate hand edit
of `pub/PubVersion.json` and `Directory.Build.props` in the same commit.

Reference implementation: **`VpnHood.Tools.ResourceTranslator`** (its own `1.x` line).

### Prerelease

Module NuGets are a **stable `X.Y.Z`** by default — the same rule as the monorepo ("NuGet is always a
stable Release version"; prerelease lines are an *app* concept). The `prerelease` input appends a bare
`-prerelease` suffix and is a **manual escape hatch**, for letting a consumer try a build before the
real release. Nothing in the normal flow sets it.

The suffix carries no counter, so two prerelease runs differ only because the build number bumped
underneath (`8.0.835-prerelease`, then `8.0.836-prerelease`). Since a prerelease publish still commits
its bump, the next stable publish just takes the next number — versions stay monotonic and can never
collide on nuget.org.

## Onboarding checklist

Six things. Everything else lives in the monorepo.

**1. `pub/PubVersion.json`** — lowercase `pub/`, matching the family layout. The module schema is a
strict subset of the monorepo's (no `Prerelease` field — prerelease is a per-run input, never
persisted state):

```json
{
  "Version": "8.0.834",
  "BumpTime": "2026-07-10T22:45:20.7288528Z"
}
```

Seed `Version` by hand at (or just below) the current monorepo version. The script hard-throws if
this file is missing.

**2. Root `Directory.Build.props`** carrying the single `<Version>`:

```xml
<Project>
	<PropertyGroup>
		<Version>8.0.834</Version>
	</PropertyGroup>
	...
</Project>
```

> **Put `<Version>` in its own leading `PropertyGroup`.** The stamper does a regex replace on the
> **first** `<Version>` in the file (`.Replace(..., 1)`). If some other `<Version>`-ish element
> precedes it, the wrong one gets rewritten.
>
> **Delete every per-csproj `<Version>`.** A csproj-level `<Version>` overrides the props file, so the
> stamp is silently ignored and the package ships the stale hardcoded number. This is the single most
> common onboarding mistake.

**3. `<IsPackable>false</IsPackable>` on every non-library project** — tests, samples, tools. Packing
is opt-**out**: any csproj without that element is published.

> The discovery filter is a regex over the raw file text:
> `-notmatch "(?i)<IsPackable>\s*false\s*</IsPackable>"`. It tolerates whitespace *inside* the
> element but **not** attributes and **not** a `Condition`. `<IsPackable Condition="...">false</IsPackable>`
> will not match, and that project gets published. Write it plain.

**4. `.github/workflows/publish_nugets.yml`** — the module's own workflow: it checks out the module
and the monorepo's `pub/` side by side, logs in to nuget.org with
[Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) (OIDC, no
stored key) and runs the shared script. Copy `VpnHood.Net.Proxies`'s; its core:

```yaml
on:
  workflow_dispatch:
    inputs:
      prerelease:
        type: boolean
        required: false
        default: false

permissions:
  contents: write   # the shared script pushes the version-bump commit back
  id-token: write   # OIDC token for the nuget.org token exchange

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
        with: { user: trudyhood }
      - shell: pwsh
        env:
          NUGET_API_KEY: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}
        run: |
          & "$env:GITHUB_WORKSPACE/vh/pub/lib/Publish-ModuleNugetPackages.ps1" `
            -moduleDir "$env:GITHUB_WORKSPACE/module" `
            -branch "${{ github.ref_name }}" `
            -prerelease:$("${{ inputs.prerelease }}" -eq "true")
```

The `NuGet/login` step has to live in the module repo: the OIDC token's `job_workflow_ref` names the
workflow that requests it, and nuget.org matches that against the policy. That is why there is no
shared reusable workflow — requested from one in `vpnhood/VpnHood`, the exchange fails with
`401: No matching trust policy`. The key is valid for one hour, so request it right before the
publish step. `user` is the nuget.org profile that created the policy; it is not a secret.

**5. The nuget.org policy** (signed in as trudyhood → Trusted Publishing):

| Field | Value |
|---|---|
| Package owner | the **`vpnhood` organization** |
| Repository owner | `vpnhood` |
| Repository | the module repo's name |
| Workflow file | `publish_nugets.yml` — **file name only** |
| Environment | empty |

The policy binds to the repository's **name** and the workflow's **file name**: renaming either breaks
publishing until the policy is replaced (`Workflow mismatch for policy ...` or `No matching trust
policy`). A private repo's new policy stays active for 7 days until its first publish binds it.

**6. Optional `_publish.ps1`** — local one-shot trigger: refuse a dirty tree → `git pull` (picks up
the last run's bump commit) → `git push` → `gh workflow run publish_nugets.yml`. CI still does all the
real work; this is only ergonomics. Copy `VpnHood.Net.Proxies/_publish.ps1` verbatim.

## Variant: modules whose payload is generated

The checklist above assumes the package content is committed. A module that generates it at publish
time (as `VpnHood.AppLib.Assets.ClassicSpa` built its `Resources/spa.zip`, until the SPA became a
sample, `VpnHood.AppUi.Spa`, 2026-09-21, that ships no package) adds its payload step to the same
workflow, before the publish step.

Two rules for the payload step: run it **before** the publish step (the script bumps and commits the
version before packing, so a payload failure afterwards would burn a version number), and do **not**
commit what it generates — the script's bump commit only stages `pub/PubVersion.json` and
`Directory.Build.props`, which is what keeps a large generated artifact out of git history.

## Requirements and gotchas

- **The repo must live under the `vpnhood` org.** The module's publish job and the monorepo's own are
  gated `if: github.repository_owner == 'vpnhood'`. Outside the org the job is **skipped
  silently and the run goes green** — it does not fail loudly. A fork that expects packages will get
  none and no error.
- **No policy, no key.** Without a matching nuget.org policy (§5) the `NuGet/login` step fails, and
  the script throws on a missing key rather than skipping the push.
- **`@develop` is a mutable pin.** Module repos ride the monorepo's `develop`, so a change there can
  break your publish without warning. That is the accepted trade for internal lockstep. This is
  explicitly **not** part of the forker/skeleton contract — forkers consume published NuGets and never
  run this publish.
- **Checkout layout matters.** The workflow puts the module in `module/` and a sparse
  monorepo checkout in `vh/`, specifically so the monorepo's own csproj files can never leak into the
  module's packable-project discovery (which is a recursive glob). Don't "simplify" either into the
  workspace root.
- **`Publish-ModuleNugetPackages.ps1` must run under pwsh 7+.** It writes `PubVersion.json` with a bare
  `Out-File`, relying on pwsh's UTF-8-no-BOM default. Under Windows PowerShell 5.1 that emits UTF-16LE
  and the next run's `ConvertFrom-Json` fails.
- **`.snupkg` symbols ride along.** Pushing a `.nupkg` also pushes its adjacent `.snupkg`; don't add a
  separate push glob for them (an explicit `**/*.nupkg` glob does *not* match `.snupkg` and is a
  common source of "symbols never published" confusion).

## Local dry run

Validate versioning + packing without touching git or nuget.org, from a sibling monorepo checkout:

```powershell
pwsh VpnHood/pub/lib/Publish-ModuleNugetPackages.ps1 -moduleDir ../MyModule -noPush
```

`-noPush` stamps the local version files and packs into `pub/bin/nuget`, skipping the bump commit and
the nuget push. Note it **does** rewrite your local `PubVersion.json` and `Directory.Build.props` —
revert them afterwards. Point `-vhVersionSource` at a local file to run offline.

## Consumers must be updated by hand

Adopting the family version can jump a module across major/minor (e.g. `7.7.828` → `8.0.835`).
Consuming `PackageReference Version="…"` entries do **not** update themselves, and there is no Central
Package Management in the monorepo yet — so grep for the package ID across every consuming repo after
a version jump, or consumers silently stay pinned to the old package.
