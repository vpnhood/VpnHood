# Module Repos — independent versioning runbook

How to give a vpnhood library its own repo and its own NuGet cadence while keeping it
**version-aligned with the monorepo family**. Companion to [RELEASE-STRATEGY.md](RELEASE-STRATEGY.md)
(which covers the monorepo's own model); this file is the step-by-step for onboarding a new module.

Reference implementation: **`VpnHood.Core.Proxies`**. Copy its shape.

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
  fit — give it a real independent version line instead.
- The monorepo version is **always read from `develop`**, hardcoded as the script's default
  (`https://raw.githubusercontent.com/vpnhood/VpnHood/develop/pub/PubVersion.json`), deliberately
  independent of the `code_ref` input — so pinning `code_ref` can never silently freeze the version
  source. `develop` always carries the highest version; `main` only advances on a stable bump.
- **The branch does not affect the version.** It only decides where the bump commit is pushed
  (`git push origin HEAD:$branch`).
- The bump is **committed before packing**, on purpose: a failed pack burns a cheap version number,
  whereas an unrecorded bump would make the next run silently `--skip-duplicate` into a no-op.

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

Five things. Everything else lives in the monorepo.

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

> **Delete every per-csproj `<Version>`.** A csproj-level `<Version>` overrides the props file, so the
> stamp is silently ignored and the package ships the stale hardcoded number. This is the single most
> common onboarding mistake.

**3. `<IsPackable>false</IsPackable>` on every non-library project** — tests, samples, tools. Packing
is opt-**out**: any csproj without that element is published.

> The discovery filter is a regex over the raw file text:
> `-notmatch "(?i)<IsPackable>\s*false\s*</IsPackable>"`. It tolerates whitespace *inside* the
> element but **not** attributes and **not** a `Condition`. `<IsPackable Condition="...">false</IsPackable>`
> will not match, and that project gets published. Write it plain.

**4. `.github/workflows/publish_nugets.yml`** — the whole caller:

```yaml
on:
  workflow_dispatch:
    inputs:
      prerelease:
        type: boolean
        required: false
        default: false

permissions:
  contents: write   # the shared module pushes the version-bump commit back

jobs:
  publish:
    uses: vpnhood/VpnHood/.github/workflows/publish_module_nugets.yml@develop
    with:
      prerelease: ${{ inputs.prerelease }}
    secrets: inherit   # org NUGET_API_KEY
```

`permissions: contents: write` must be granted **by the caller** — the reusable workflow declaring it
is not enough.

**5. Optional `_publish.ps1`** — local one-shot trigger: refuse a dirty tree → `git pull` (picks up
the last run's bump commit) → `git push` → `gh workflow run publish_nugets.yml`. CI still does all the
real work; this is only ergonomics. Copy `VpnHood.Core.Proxies/_publish.ps1` verbatim.

## Requirements and gotchas

- **The repo must live under the `vpnhood` org.** Both the reusable job and the monorepo's own publish
  job are gated `if: github.repository_owner == 'vpnhood'`. Outside the org the job is **skipped
  silently and the run goes green** — it does not fail loudly. A fork that expects packages will get
  none and no error.
- **`NUGET_API_KEY` must be exposed to the repo** (org-level secret + `secrets: inherit`). Inside the
  org a missing key is a hard throw, not a warn-and-skip.
- **`@develop` is a mutable pin.** Module repos ride the monorepo's `develop`, so a change there can
  break your publish without warning. That is the accepted trade for internal lockstep. This is
  explicitly **not** part of the forker/skeleton contract — forkers consume published NuGets and never
  call this workflow.
- **Checkout layout matters.** The reusable workflow puts the module in `module/` and a sparse
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
