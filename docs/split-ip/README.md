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
SplitDbPublisherService.Publish                             SplitDbManifest.Read(folder)
  ├─ SplitCountryService.EnsureSplitIpDb               └─ one SqliteIpFilter per listed db
  ├─ SplitIpViaAppService.EnsureSplitIpDb                     (read-only, per-packet)
  └─ SplitDbManifest.Write(folder, dbPaths)                 owned by the SqliteIpFilterChain stage
       └─ each: SplitIpDbBuilder.EnsureAsync
            └─ rebuild only if meta says stale
```

1. **Before connecting**, `SplitDbPublisherService.Publish` asks each context's service for its db and
   publishes the active set through the folder's **manifest** (`SplitDbManifest`). Each service owns
   its WHOLE activity decision — its user setting (`SplitCountryMode` ≠ `IncludeAll`;
   `UseSplitIpViaApp`; `UseSplitDomain`) and the premium plan (`IPremiumFeatureChecker`) — and answers
   with a path or null. An inactive context contributes no entry — off is the empty case of the same
   flow, and a stale db file lying in the folder means nothing (presence on disk is never policy). The
   manifests are written only by the publisher: the ip folder is shared by two services, and a
   one-service write would sweep its sibling's db. Failures propagate and fail the connect
   (fail-closed): a split the user configured is enforced or the connection does not proceed, never
   silently skipped.

2. **Nothing travels cross-process.** Each db is **self-describing** — it stores up to three sets
   (include, exclude, block) and which sets are populated *is* its semantic — and each filter folder's
   manifest says which dbs are current, so vpn.config and the reconfigure request carry no filter
   payload at all: the service resolves everything from the folders it already reads.

3. **At runtime**, the filter pipes hold one `SqliteIpFilter` / `SqliteDomainFilter` gate per listed
   db, owned by a self-updating stage (`SqliteIpFilterChain` / `SqliteDomainFilterChain`) whose paths provider
   re-reads the folder's manifest. Pipe order (outermost first):

   ```text
   CachedIpFilter (60-min per-endpoint memo)
     └─ StaticIpFilter (server ∩ device allow set)
          └─ SqliteIpFilterChain (owns the gates; swaps them on Reconfigure)
               ├─ platform NetFilter (optional, permanent — runs first, survives swaps)
               └─ gates: SqliteIpFilter (split-country) → SqliteIpFilter (split-ip-via-app)

   CachedDomainFilter (60-min per-domain memo)
     └─ SqliteDomainFilterChain (owns the gates; swaps them on Reconfigure)
          ├─ gates: SqliteDomainFilter (split-domain) — run first (Include override lane)
          └─ platform DomainFilter (optional, permanent — survives swaps)
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

## Live updates (no reconnect)

Split changes apply to a RUNNING session through the existing reconfigure flow — the dbs stay
immutable; what changes is which files are current:

1. The app rebuilds (or reuses) the dbs and rewrites each folder's manifest. File names carry the
   source-signature hash, so a changed source builds a **new** file while the service still holds the
   old one open; the manifest write is atomic (temp + rename) and also sweeps superseded files the
   service no longer holds. An always-on restart starts from the same manifests.
2. `VpnServiceManager.Reconfigure` only signals the service — the request carries no filter payload.
3. The service host rolls the `Reconfigure()` command down the filter pipes — the command twin of
   `Changed`: **Reconfigure rolls down, Changed rolls up**. Wrapping stages just forward it; the
   split filter stage re-reads its folder's manifest through its paths provider, no-ops when its own
   paths are unchanged, and otherwise swaps its gates: in-flight lookups drain on the old gates (the
   guarantee is the stage's own, independent of what the gates are made of), the superseded gates are
   disposed and the stage deletes their db files (it was the process holding them open). The
   permanent inner (platform) filter is never recreated. The swap raises `Changed`, each wrapping
   stage rolls it up the pipe, and the cached stages drop their memoized verdicts by themselves — no
   verdict outlives the gates that produced it. Nobody re-toggles SNI extraction either: the client
   re-derives it from its own pipe (`IDomainFilter.IsEmpty`) on every `Changed`, so rules appearing
   or vanishing switch extraction on and off with no outside caller.
4. Flows already decided keep their verdict (open connections, the QUIC flow cache until its
   timeout); new lookups see the new rules.

Triggers: any UserSettings save while connected (country mode/selection, via-app and domain toggles),
and the split text-file settings (`SplitIpViaAppSettings.Set` / `SplitDomainSettings.Set` raise change
events that `VpnHoodApp` answers with a reconfigure — the files are outside UserSettings). The
device-level splits (`UseSplitIpViaDevice`, `UseSplitLocalNetwork`, per-app split) still require a
reconnect — they configure the OS adapter — so a change while connected only flags the session
(`AppState.IsReconnectRequired`) and the UI offers a reconnect.

## Storage layout

```text
<IDevice.VpnServiceConfigFolder>/ip-filters/manifest.json          ← which dbs are active (the policy)
<IDevice.VpnServiceConfigFolder>/ip-filters/split-country.<sig-hash>.db
<IDevice.VpnServiceConfigFolder>/ip-filters/split-ip-via-app.<sig-hash>.db
<IDevice.VpnServiceConfigFolder>/domain-filters/manifest.json
<IDevice.VpnServiceConfigFolder>/domain-filters/split-domain.<sig-hash>.db
```

`VpnServiceConfigFolder` is the folder the VpnService already reads its config from — the one location
guaranteed readable by both processes. On iOS it lives inside the shared app-group container (the only
path a Network Extension can read). Filenames are per context and **versioned by the source-signature
hash**: an unchanged source keeps its file (reuse), a changed one builds a sibling the running service
can swap to. Each folder's `manifest.json` (written by `SplitDbManifest`) is the only statement of
which dbs apply — a db file not listed there is inert garbage, never policy. Deletion is split by who
held what: on a live swap the service's chain stage deletes the files it just closed (nobody else
could — it had them open); the app's manifest write sweeps the rest (crash remnants, files superseded
while disconnected). The builder only manages the file it is asked about. A schema change rebuilds
each db in place (its `schema_version` meta no longer matches).

## Future work

- Server-not-included block/split policy as its own pipe filter (kept out of `SqliteIpFilter` by
  design).
