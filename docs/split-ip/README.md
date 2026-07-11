# Split-IP filtering (SQLite-backed)

How split-ip filtering works end to end: the app process builds a small on-disk SQLite db per
**context**, and the VpnService answers per-packet membership from it. This replaced materializing the
selections into `ClientOptions.IncludeIpRangesByApp` (~97MB of `IpRange[]`, JSON-shipped cross-process
and re-deserialized), which exceeded the iOS Network Extension memory limit.

This doc is the cross-scope map: the shared flow, the cross-process contract, and the filter pipe. What
each context stores and when it rebuilds is that context's policy — see its own doc:

| Context | Db | Policy doc |
| --- | --- | --- |
| split-country | `split-country.db` | [split-country.md](split-country.md) |
| split-ip-via-app | `split-ip-via-app.db` | [split-ip-via-app.md](split-ip-via-app.md) |
| split-domain | `split-domain.db` | [split-domain.md](split-domain.md) |

The db format, meta/rebuild rules, and filter semantics are the contract of the
`VpnHood.Core.Filtering.Sqlite` project — see its
[README](../../src/Core/VpnHood.Core.Filtering.Sqlite/README.md).

## Flow

```text
App process (VpnHoodApp / AppLib)                    VpnService process (VpnHoodClient)
─────────────────────────────────                    ──────────────────────────────────
VpnHoodApp.PrepareSplitIpDbs                         ClientOptions.SplitIpDbPaths[]
  ├─ SplitCountryService.EnsureSplitIpDb               └─ one SqliteIpFilter per path
  ├─ SplitIpViaAppService.EnsureSplitIpDb                     (read-only, per-packet)
  └─ each: SplitIpDbBuilder.EnsureAsync                     chained into the filter pipe
       └─ rebuild only if meta says stale
```

1. **Before connecting**, `VpnHoodApp.PrepareSplitIpDbs` / `PrepareSplitDomainDbs` ask each context's
   service to build or reuse its db, gated by that context's user setting (`SplitCountryMode` ≠
   `IncludeAll`; `UseSplitIpViaApp`; `UseSplitDomain`). A context that is off contributes no path and
   its db is skipped entirely. Failures propagate and fail the connect (fail-closed): a split the user
   configured is enforced or the connection does not proceed, never silently skipped.

2. **Only paths travel, not the ranges.** Each db is **self-describing** — it stores up to three sets
   (include, exclude, block) and which sets are populated *is* its semantic — so `ClientOptions`
   carries only `SplitIpDbPaths` and `SplitDomainDbPaths`; the cross-process payload is file paths
   instead of megabytes of ranges (and no per-db action, either).

3. **At runtime**, `VpnHoodClient` chains one `SqliteIpFilter` / `SqliteDomainFilter` per path into
   its two filter pipes. Pipe order (outermost first):

   ```text
   CachedIpFilter (60-min per-endpoint memo)
     └─ StaticIpFilter (server ∩ device allow set)
          └─ SqliteIpFilter (split-ip-via-app: include/exclude/block sets)
               └─ SqliteIpFilter (split-country: include or exclude set)
                    └─ platform NetFilter (optional)

   CachedDomainFilter (60-min per-domain memo)
     └─ SqliteDomainFilter (split-domain: include/exclude/block sets)
          └─ platform DomainFilter (optional)
   ```

   The domain pipe runs first (on the extracted SNI); any non-`Default` domain verdict preempts the IP
   pipe entirely.

   IP composition rule: each stage runs its inner `next` first and returns the first non-`Default`
   verdict. **Every IP gate is a veto**: it may return `Exclude` (bypass) or `Block` (drop), and
   `Default` means "no objection". Undecided traffic tunnels — fail-closed for a VPN: a missing or
   empty gate keeps traffic inside the tunnel, it never leaks around it. A non-empty include set vetoes
   non-members, so chained include sets compose as set **intersection**: an address tunnels only if
   every active include set contains it. Within one db the precedence is block > exclude >
   include-veto.

   `FilterAction.Include` is never returned by IP gates. It survives only as an explicit override
   lane: the **domain include set** and the ICMP force use it to push traffic through the tunnel past
   every gate. Domain gates therefore consult their own sets before `next` (an include override must
   not be second-guessed), and "tunnel nothing except aaa.com" is expressed as domain-include
   `aaa.com` + via-app IP-exclude `0.0.0.0/0, ::/0` — see [split-domain.md](split-domain.md).

## Rebuilds and change detection

`SplitIpDbBuilder.EnsureAsync` reuses a db when its meta table matches the context's
`source_signature` — an opaque string each context's builder composes so that it changes iff the
stored sets would change (see the context docs for what goes into it). Ordinary connects hit this check
and never touch the source (zip or text files). Source changes trigger a one-shot rebuild in the app
process — build to temp file, atomic rename, so no torn db and no orphans.

## Storage layout

```text
<IDevice.VpnServiceConfigFolder>/ip-filters/split-country.db
<IDevice.VpnServiceConfigFolder>/ip-filters/split-ip-via-app.db
<IDevice.VpnServiceConfigFolder>/domain-filters/split-domain.db
```

`VpnServiceConfigFolder` is the folder the VpnService already reads its config from — the one location
guaranteed readable by both processes. On iOS it lives inside the shared app-group container (the only
path a Network Extension can read). Filenames are static and per context, not versioned and not per
selection: connections are exclusive, so the file is never in use at rebuild time. A schema change
rebuilds each db in place (its `schema_version` meta no longer matches); if a db file is ever RENAMED,
deleting the old file is the renamer's responsibility — the builder only manages the file it is asked
about.

## Future work

- Server-not-included block/split policy as its own pipe filter (kept out of `SqliteIpFilter` by
  design).
