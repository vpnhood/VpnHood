# Split-IP filtering (SQLite-backed)

How split-ip filtering works end to end: the app process builds a small on-disk SQLite db per
**context**, and the VpnService answers per-packet membership from it. This replaced materializing the
selections into `ClientOptions.IncludeIpRangesByApp` (~97MB of `IpRange[]`, JSON-shipped cross-process
and re-deserialized), which exceeded the iOS Network Extension memory limit.

This doc is the cross-scope map: the shared flow, the descriptor contract, and the filter pipe. What
each context stores and when it rebuilds is that context's policy — see its own doc:

| Context | Dbs | Policy doc |
| --- | --- | --- |
| split-country | `split-country.db` | [split-country.md](split-country.md) |
| split-ip-via-app | `split-ip-via-app.db` + `split-ip-via-app-blocks.db` | [split-ip-via-app.md](split-ip-via-app.md) |

The db format, meta/rebuild rules, and filter semantics are the contract of the
`VpnHood.Core.Filtering.Sqlite` project — see its
[README](../../Src/Core/VpnHood.Core.Filtering.Sqlite/README.md).

## Flow

```text
App process (VpnHoodApp / AppLib)                    VpnService process (VpnHoodClient)
─────────────────────────────────                    ──────────────────────────────────
VpnHoodApp.PrepareSplitIpDbs                         ClientOptions.SplitIpDbFilters[]
  ├─ SplitCountryService.EnsureSplitIpDb               └─ one SqliteIpFilter per entry
  ├─ SplitIpViaAppService.EnsureSplitIpDbs                    (read-only, per-packet)
  │    (allow set + blocks)                                 chained into the filter pipe
  └─ each: SplitIpDbBuilder.EnsureAsync
       └─ rebuild only if meta says stale
```

1. **Before connecting**, `VpnHoodApp.PrepareSplitIpDbs` asks each context's service to build or reuse
   its dbs, gated by that context's user setting (`SplitCountryMode` ≠ `IncludeAll`; `UseSplitIpViaApp`).
   A service yields one descriptor per db — the `FilterAction` the client should apply to membership;
   a context that is off contributes no descriptors and its dbs are skipped entirely. Failures propagate
   and fail the connect (fail-closed): a split the user configured is enforced or the connection does
   not proceed, never silently skipped.

2. **The descriptors travel, not the ranges.** `ClientOptions` carries only `SplitIpDbFilters` — an
   array of `{ DbPath, Action }`; the cross-process payload is paths and enums instead of megabytes of
   ranges.

3. **At runtime**, `VpnHoodClient` chains one `SqliteIpFilter` per descriptor into its filter pipe.
   Pipe order (outermost first):

   ```text
   CachedIpFilter (60-min per-endpoint memo)
     └─ StaticIpFilter (server ∩ device allow set; grants Include)
          └─ SqliteIpFilter (split-ip-via-app blocks gate; Block action)
               └─ SqliteIpFilter (split-ip-via-app allow gate; Include action)
                    └─ SqliteIpFilter (split-country gate; Include or Exclude action)
                         └─ platform NetFilter (optional)
   ```

   Composition rule: each stage runs its inner `next` first and returns the first non-`Default` action.
   "Tunnel" is expressed as `Default` (defer) so every gate applies; only the terminal `StaticIpFilter`
   grants `Include`. `Exclude`/`Block` are superior. Chained `Include` gates therefore compose as set
   **intersection**: an address tunnels only if every active gate contains it.

## Rebuilds and change detection

`SplitIpDbBuilder.EnsureAsync` reuses a db when its meta table matches the context's
`source_signature` — an opaque string each context's builder composes so that it changes iff the
stored set would change (see the context docs for what goes into it). Ordinary connects hit this check
and never touch the source (zip or text files). Source changes trigger a one-shot rebuild in the app
process — build to temp file, atomic rename, so no torn db and no orphans.

## Storage layout

```text
<IDevice.VpnServiceConfigFolder>/ip-filters/split-country.db
<IDevice.VpnServiceConfigFolder>/ip-filters/split-ip-via-app.db
<IDevice.VpnServiceConfigFolder>/ip-filters/split-ip-via-app-blocks.db
```

`VpnServiceConfigFolder` is the folder the VpnService already reads its config from — the one location
guaranteed readable by both processes. On iOS it lives inside the shared app-group container (the only
path a Network Extension can read). Filenames are static and per context, not versioned and not per
selection: connections are exclusive, so the file is never in use at rebuild time.

## Future work

- `split-domain.db` as a sibling context reusing the same `Filtering.Sqlite` infrastructure — one db
  and one pipe stage per context.
- Server-not-included block/split policy as its own pipe filter (kept out of `SqliteIpFilter` by
  design).
