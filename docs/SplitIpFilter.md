# Split-IP filtering (SQLite-backed)

How split-country IP filtering works end to end: the app process builds a small on-disk SQLite db of
the selected ranges, and the VpnService answers per-packet membership from it. This replaced
materializing the selection into `ClientOptions.IncludeIpRangesByApp` (~97MB of `IpRange[]`, JSON-shipped
cross-process and re-deserialized), which exceeded the iOS Network Extension memory limit.

This doc is the cross-scope map. The db format, rebuild rules, and filter semantics are the contract of
the `VpnHood.Core.Filtering.Sqlite` project — see its
[README](../Src/Core/VpnHood.Core.Filtering.Sqlite/README.md).

## Flow

```text
App process (VpnHoodApp / AppLib)                    VpnService process (VpnHoodClient)
─────────────────────────────────                    ──────────────────────────────────
SplitCountryService.EnsureSplitIpDb(dbPath)          ClientOptions.SplitIpDbPath / SplitIpAction
  ├─ SplitCountryMode → countries + FilterAction       └─ SqliteIpFilter (read-only, per-packet)
  ├─ invert to the smaller set (see below)                  chained into the filter pipe
  └─ SplitIpDbManager.EnsureAsync
       └─ rebuild only if meta says stale
```

1. **Before connecting**, `VpnHoodApp.PrepareSplitIpDb` calls
   `SplitCountryService.EnsureSplitIpDb(dbPath, ct)`, which maps the user's `SplitCountryMode` to a
   country set and a `FilterAction`:

   | SplitCountryMode | Countries | Action |
   | --- | --- | --- |
   | `IncludeAll` | — | `Default` (no db, filter not created) |
   | `IncludeList` | `UserSettings.SplitCountries` | `Include` |
   | `ExcludeList` | `UserSettings.SplitCountries` | `Exclude` |
   | `ExcludeMyCountry` | client country (fresh lookup, no cache/server hint) | `Exclude` |

   On any failure it logs, resets the mode to `IncludeAll`, and returns `Default` — the connection
   proceeds unfiltered rather than failing.

2. **The descriptor travels, not the ranges.** `ClientOptions` carries only `SplitIpDbPath` +
   `SplitIpAction`; the cross-process payload is a path and an enum instead of megabytes of ranges.

3. **At runtime**, `VpnHoodClient` chains a `SqliteIpFilter` into its filter pipe when the descriptor is
   set. Pipe order (outermost first):

   ```text
   CachedIpFilter (60-min per-endpoint memo)
     └─ StaticIpFilter (server ∩ device ∩ app allow set; block list; grants Include)
          └─ SqliteIpFilter (country membership gate; never returns Include)
               └─ platform NetFilter (optional)
   ```

   Composition rule: each stage runs its inner `next` first and returns the first non-`Default` action.
   "Tunnel" is expressed as `Default` (defer) so every gate applies; only the terminal `StaticIpFilter`
   grants `Include`. `Exclude`/`Block` are superior.

## The inversion rule (smaller-set short path)

The db stores ranges as-is; the action gives them meaning. A selection and its complement (against the
asset's available countries) therefore express the same split, so
`SplitCountryService.ResolveSplitIpDbSelection` always stores the **strictly smaller** set and flips
`Include`↔`Exclude` to match. "All countries except one" — whether the UI sends 243 includes or one
exclude — stores one country and builds in milliseconds. Ties don't invert (deterministic); selected
codes unknown to the asset are dropped before comparing; `Block` is never inverted (no complement form).

Consequence: addresses covered by *no* country in the asset (unallocated space) follow the flipped
action's default. For "everything except X" they are tunneled, which matches user intent.

## Rebuilds and change detection

`SplitIpDbManager.EnsureAsync` reuses the db when its meta table matches the current asset hash
(the zip's `_checksum.txt`, falling back to an MD5 of the zip bytes) and the stored selection
signature. Ordinary connects hit this check and never open the zip. Selection or asset changes trigger
a one-shot rebuild in the app process — build to temp file, atomic rename, so no torn db and no orphans.

## Storage layout

```text
<IDevice.VpnServiceConfigFolder>/ip-filters/split-country.db
```

`VpnServiceConfigFolder` is the folder the VpnService already reads its config from — the one location
guaranteed readable by both processes. On iOS it lives inside the shared app-group container (the only
path a Network Extension can read). Filenames are static and per context, not versioned and not per
country: connections are exclusive, so the file is never in use at rebuild time.

## Performance (measured)

Real asset (14MB zip, 244 countries, 618k ranges), Android x86_64 emulator, Release:

| Build | Time |
| --- | --- |
| One country (US, largest) | 120 ms |
| Half the world (122 countries — worst case after inversion) | 358 ms |
| All 244 countries (cannot occur after inversion) | 652 ms |

Real ARM devices are ~2–4× slower — worst realistic case stays well under 2s, so the build runs inline
in the connect flow (surfaced via `SplitCountryService.IsBusy` → `AppConnectionState.Initializing`)
without a progress UI. Runtime queries are single B-tree seeks, memoized per endpoint by
`CachedIpFilter`.

## Future work

- `split-app-ip.db` (per-app IP splits) and `split-domain.db` as sibling contexts reusing the same
  `Filtering.Sqlite` infrastructure — one db and one pipe stage per context.
- Server-not-included block/split policy as its own pipe filter (kept out of `SqliteIpFilter` by
  design).
