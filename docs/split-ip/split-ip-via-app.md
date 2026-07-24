# Split-IP-via-app filtering

Policy of the **split-ip-via-app** context (`UseSplitIpViaApp`): how the user's IP filter text files
become `split-ip-via-app.db` and what membership means. The shared architecture (filter pipe, storage,
rebuild mechanics) is in [README.md](README.md); the db format is in the
[Filtering.Sqlite README](../../src/Core/VpnHood.Core.Filtering.Sqlite/README.md).

Naming: "via app" says WHERE the IP split is enforced — by the app's own filter pipe inside the tunnel,
as opposed to `UseSplitIpViaDevice`, where the split decides which ranges the OS routes into the VPN
adapter at all. It has nothing to do with splitting *applications* — that is the separate `SplitApps` /
`SplitAppMode` feature (per-application tunneling).

## Sources

Three user-edited text files under `<storage>/splits/ips_via_app/` (managed by
`SplitIpViaAppSettings`, validated on write, premium-gated by `AppFeature.SplitIpViaApp`):

| File | Meaning when non-empty |
| --- | --- |
| `includes.txt` | Only these ranges are tunnel-eligible (empty ⇒ no constraint). |
| `excludes.txt` | These ranges bypass the tunnel (empty ⇒ None). |
| `blocks.txt` | These ranges are dropped entirely at app level (empty ⇒ None). |

The on/off gate is `UserSettings.UseSplitIpViaApp`, checked together with the premium feature by the
service itself (it returns null when inactive) — there is no emptiness short path. Missing files count as
empty, and empty sources leave the db's sets empty, which is a no-op gate that routes identically to
no filter.

## One db, three sets that mirror the files

`SplitIpViaAppService.EnsureSplitIpDb` stores each source file into its own set of the single
`split-ip-via-app.db`, exactly as written — no merge algebra:

- **include** — a non-empty set vetoes non-members (`Exclude`); members pass as `Default`. Chained
  with the country include set this composes as intersection.
- **exclude** — members bypass the tunnel (`Exclude`). Checked before the include set, so an address in
  both is excluded — the same outcome the old `All ∩ includes − excludes` merge produced.
- **block** — members are dropped entirely (`Block`). Superior to both other sets, so a blocked address
  is dropped even if the include set contains it.

Each table answers "what did the user write", which makes the db directly inspectable, and the build is
a plain parse-and-insert per file. No inversion rule applies here (unlike
[split-country](split-country.md)): inverting an arbitrary range list yields about the same number of
rows (`|¬S| ≤ |S| + 2`), so storing the complement could never make the db meaningfully smaller. The
row count is proportional to what the user wrote, nothing more.

## Change detection

`source_signature` = mtime + length of all three source files
(`includes:<ticks>:<len>,excludes:…,blocks:…`). This is stat-only: ordinary connects never
read — let alone parse — the text files; the parse runs only on the rare rebuild path
(`IpRangeListDbBuilder` takes every set as a *factory*). Every settings write rewrites the file, so
the signature always moves with the content; a spurious touch just costs one fast rebuild.

## Failure policy

Failures propagate and fail the connect (fail-closed): a split the user configured is enforced or the
connection does not proceed, never silently skipped. The user's text files are never modified — they
are the source of truth the user typed in.
