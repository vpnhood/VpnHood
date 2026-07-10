# Split-IP-via-app filtering

Policy of the **split-ip-via-app** context (`UseSplitIpViaApp`): how the user's IP filter text files
become `split-ip-via-app.db` + `split-ip-via-app-blocks.db` and what membership means. The shared
architecture (descriptors, filter pipe, storage, rebuild mechanics) is in [README.md](README.md); the
db format is in the
[Filtering.Sqlite README](../../Src/Core/VpnHood.Core.Filtering.Sqlite/README.md).

Naming: "via app" says WHERE the IP split is enforced — by the app's own filter pipe inside the tunnel,
as opposed to `UseSplitIpViaDevice`, where the split decides which ranges the OS routes into the VPN
adapter at all. It has nothing to do with splitting *applications* — that is the separate `SplitApps` /
`SplitAppMode` feature (per-application tunneling).

## Sources

Three user-edited text files under `<storage>/ip_filters/` (managed by `SplitIpSettings`, validated on
write, premium-gated by `AppFeature.SplitIpViaApp`):

| File | Meaning when non-empty |
| --- | --- |
| `app_includes.txt` | Only these ranges are tunnel-eligible (empty ⇒ All). |
| `app_excludes.txt` | These ranges bypass the tunnel (empty ⇒ None). |
| `app_blocks.txt` | These ranges are dropped entirely at app level (empty ⇒ None). |

The on/off gate is `UserSettings.UseSplitIpViaApp` (checked together with the premium feature by
`VpnHoodApp.PrepareSplitIpDbs`) — the service has no emptiness short path. Missing files count as
empty, and empty sources build no-op dbs that route identically to no filter.

## Two dbs: one merged allow set (`Include`) + one block set (`Block`)

`SplitIpViaAppService.EnsureSplitIpDbs` prepares both and returns their descriptors:

- **`split-ip-via-app.db`** — includes and excludes merged into ONE range list:

  ```text
  stored set = All ∩ includes − excludes
  ```

  stored with the `Include` action: membership means "eligible for tunnel", non-members bypass. There
  is no include/exclude pair at runtime, so the db needs no mode and a selection has exactly one
  canonical stored form.

- **`split-ip-via-app-blocks.db`** — the blocks list as-is, stored with the `Block` action: members
  are dropped entirely. `Block` is superior in the pipe, so a blocked address is dropped even if the
  allow set contains it.

No inversion rule applies here (unlike [split-country](split-country.md)): inverting an arbitrary
range list yields about the same number of rows (`|¬S| ≤ |S| + 2`), so storing the complement could
never make the db meaningfully smaller. The row count is proportional to what the user wrote, nothing
more.

## Change detection

Each db has its own `source_signature` = mtime + length of its source files
(`app_includes:<ticks>:<len>,app_excludes:…` for the allow db; `app_blocks:…` for the block db).
This is stat-only: ordinary connects never read — let alone parse — the text files; the parse/merge
runs only on the rare rebuild path (`IpRangeListDbBuilder` takes both inputs as *factories*). Every
settings write rewrites the file, so the signature always moves with the content; a spurious touch
just costs one fast rebuild.

## Failure policy

Failures propagate and fail the connect (fail-closed): a split the user configured is enforced or the
connection does not proceed, never silently skipped. The user's text files are never modified — they
are the source of truth the user typed in.
