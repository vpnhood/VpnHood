# TEMP PLAN — SQLite-backed split-IP filter

> **Temporary working doc.** Delete or fold into a permanent note once the feature lands.
> Captures the design agreed in conversation. No code committed yet (no-auto-commit).

## IMPLEMENTATION STATUS (in progress)

- ✅ **Step 1** — `IpRangeOrderedList.DeserializeRaw` streaming reader + parity test (`IpNetworkTest.DeserializeRaw_matches_Deserialize`).
- ✅ **Step 2** — new project `VpnHood.Core.Filtering.Sqlite` (Microsoft.Data.Sqlite.Core 10.0.9 + **bundle_e_sqlite3 3.0.3**).
  **Security:** the 3.0.3 bundle ships patched e_sqlite3 for ALL RIDs incl. `.android`/`.ios` → fixes GHSA-2m69-gcr7-jv3q
  (Android head verified: no vulnerable packages, no NU1603). No stable 2.1.12 bundle exists; MDS.Core 10.0.9 needs
  only `SQLitePCLRaw.core >= 2.1.11`, which 3.0.x satisfies — verified compatible by the DB tests. NO per-package lib override.
  `SplitIpDb` (schema/keys/conversion), `SplitIpSqlite` (init), `SplitIpDbBuilder`, `SplitIpDbManager`. Tests in `SplitIpDbTest`.
- ✅ **Step 3** — `SqliteIpFilter` lean gate (v4 INTEGER / v6 BLOB, per-thread read-only conns). Membership tests in `SplitIpDbTest`.
- ✅ **Step 4** — `ClientOptions.SplitIpDbPath`/`SplitIpAction` added; `VpnHoodClient` wires `SqliteIpFilter` as the
  `StaticIpFilter`'s inner when a db path is set and the action is non-Default. Client refs `Filtering.Sqlite`.
  **`SplitIpMode` was removed** — the descriptor now uses `FilterAction` directly (see Runtime filter section).
  **Deviation:** kept `VpnHoodClient.SessionIncludeIpRangesByApp` — it now exposes only the small allow set and still
  serves `ServerNetFilterConfigTest` correctly (no country in that test), so removing it is churn without gain.
  **No change needed** in `ClientSessionBuilder`: its intersection logic is unchanged; `IncludeIpRangesByApp` is just small now.
- ✅ **Step 5** — app side. New `SplitCountryService.EnsureSplitIpDb(dbPath, ct)` replaces `LocationService.GetIncludeCountryIpRanges`
  (decoupled: LocationService stays a pure region provider; SplitCountryService uses it only as a data source):
  maps `SplitCountryMode` → countries + `FilterAction` (IncludeList→Include, ExcludeList/ExcludeMyCountry→Exclude,
  IncludeAll→Default/no db), assetHash = zip `_checksum.txt` (fallback MD5 of zip bytes), calls
  `SplitIpDbManager.EnsureAsync`; on failure falls back to IncludeAll (same policy as before).
  `VpnHoodApp.GetIncludeIpRanges` is now sync and returns only the small SplitIpViaApp set;
  `PrepareSplitIpDb` builds the descriptor with db at `<IDevice.VpnServiceConfigFolder>/ip-filters/split-country.db`
  (on iOS that folder is inside the shared app-group container — the only path the NE can read).
  `ConnectInternal2` fills `SplitIpDbPath`/`SplitIpAction`. AppLib.App now refs `Filtering.Sqlite`.

All tests green so far (13).

**Build perf (measured, Android x86_64 emulator, Release):** the `SqliteCommand` insert path cost ~30µs/row
on Mono (all 244 countries = 16.8s — over the 4s "needs progress UI" bar). Rewritten hot loop with raw
SQLitePCL bind/step/reset on `connection.Handle`: US=120ms, half-of-world=358ms, all-244=652ms (618k rows,
30MB db). Real ARM devices ~2-4x slower ⇒ worst realistic case (half, due to inversion) well under 2s —
**no ProgressMonitor needed**. Desktop CoreCLR: ~1s ADO / ~0.3s raw for all countries.

## Goal

Stop materializing / JSON-shipping the ~97 MB country `IpRange[]` (`ClientOptions.IncludeIpRangesByApp`).
Instead the **app process** builds a small on-disk SQLite db of the *currently-selected* ranges, and the
**VpnService** answers membership per destination endpoint via an `IIpFilter`, shielded by the existing
`CachedIpFilter` (60-min per-endpoint memo).

Why: deserializing all countries into `IpRangeOrderedList` costs ~97 MB resident (each range = `IpRange`
object + 2 `IPAddress` objects); it is then JSON-serialized and shipped across the process boundary to the
VpnService and re-deserialized. That blows the ~50 MB iOS Network-Extension jetsam limit and wastes time.

## Governing principle (db granularity)

**One db = one independent rebuild unit = one context.** Share a db only among data always built and
invalidated together. Split into multiple *tables* within a db for schema/query reasons, never for lifecycle.

Rebuild model is "build temp file → atomic-rename → immutable at runtime", so the **file** is the unit of
rebuild/atomicity/immutability. Mixing cadences in one file forces either coupled whole-file rebuilds or
in-place mutation (DELETE+INSERT → fragmentation → VACUUM → locking vs the open VpnService handle).

### Per-context db (decision)

`country.db` now; `app-ip.db` and `domain.db` later. Each rebuilds via its own atomic-rename without touching
the others (editing the app list never rebuilds country). Read-only immutable connections opened lazily per
active context; no cross-context transactions ever needed.

Same db is right only for same-context sub-data sharing the exact rebuild: `range_v4` + `range_v6` + `meta`
live together in `country.db`.

## Core architectural principle (pipe composition)

The filter pipe is priority-based ("first non-`Default` wins"; each filter runs its inner `next` first).
To make it behave like the old intersection:

> **Only the terminal granter returns `Include`. Every context filter is a *gate* returning
> `Block`/`Exclude`/`Default` — never `Include`.**

Pipe:
```
CachedIpFilter(
  StaticIpFilter {                         // terminal granter — small, EPHEMERAL only
     Include = server ∩ device,            //   from HelloResponse; never persisted, never big
     Exclude = appExclude, Block = appBlock,   // small user sets, for now (in-memory)
     next = SqliteIpFilter(country.db, mode)   // ← gate: Block/Exclude/Default only
  })
```

`SqliteIpFilter` gate return (never `Include`):
```
include-list mode: member ? Default : Exclude   // non-selected ⇒ bypass
exclude mode:      member ? Exclude : Default    // selected     ⇒ bypass
```
`StaticIpFilter` runs its inner (gate) first; gate bypass/block wins; if gate neutral, terminal grants
`Include` for `server ∩ device`. Reproduces `country ∩ server ∩ device ∩ appInclude \ appExclude`.
Adding a context later = insert one more `SqliteIpFilter` gate (order among gates doesn't matter — they AND;
`Block` short-circuits).

- `StaticIpFilter` stays as terminal, **holding only small ephemeral `server ∩ device`** — country/app/domain
  big data are all `SqliteIpFilter` gates. (Server/device ranges are transient per-connect, tiny → memory, not db.)
- IncludeAll mode ⇒ no country gate at all (`next = netFilter?.IpFilter`, usually null); terminal Include for
  `server ∩ device` = current behavior unchanged.

**RESOLVED:** keep the ephemeral server/device gate in `StaticIpFilter`; `SqliteIpFilter` stays **lean
(country membership only)**. Do NOT funnel the small in-memory ranges through SQLite, and do NOT cram other
concerns (server routability, blocks) into `SqliteIpFilter` — new concerns go in as **their own pipe filter**
(see "Deferred: server-not-included policy").

### Deferred: server-not-included policy (block vs split) — SEPARATE pipe, LATER

Today, an address outside the server include set falls through the terminal to `Default` ⇒ **bypass (split
direct)** — that's the current behavior and stays the default. Two future enhancements, both **deferred**, and
both to be built as **their own pipe filter — NOT inside `SqliteIpFilter`**:

1. **`ServerNotIncludedAction` = Split | Block** (client-only, no protocol change): a new
   `UserSettings` flag ("strict tunneling / block unroutable traffic"). Implemented as a small dedicated
   filter (e.g. `ServerIncludeFilter`) inserted in the pipe: `addr ∉ serverInclude ⇒ Block (strict)` or
   `Exclude (split)`; else `Default`. Priority: user-split choices (country/app) run inner (higher) so a
   deliberately-split address is never force-blocked; only would-be-tunneled traffic is subject to the policy.
2. **Server-sent block list** (protocol change): add `BlockedIpRanges` to `HelloResponse` so the server can
   communicate explicit blocks distinct from "not included"; client hard-blocks those. Needs a version bump.

Rationale for keeping these out of `SqliteIpFilter`: single responsibility (country db lookup), and correct
priority ordering is clearer as explicit pipe stages than as branches inside the db filter.

## Components

| Where | What | Now? |
|---|---|---|
| **new** `VpnHood.Core.Filtering.Sqlite` | `SqliteIpFilter : IIpFilter` (gate) + `SplitIpDbBuilder` + `SplitIpDbManager`. Refs `Microsoft.Data.Sqlite.Core`, Filtering.Abstractions, Toolkit. | ✅ |
| `VpnHood.Core.Toolkit/Net/IpRangeOrderedList.cs` | Add `DeserializeRaw` streaming reader (no alloc, no sort). | ✅ |
| `VpnHood.Core.Client.Abstractions/ClientOptions.cs` | Per-context descriptor: `SplitIpDbPath?` + `SplitIpAction` (`FilterAction`); keep small `BlockIpRangesByApp` / app arrays for now; drop giant `IncludeIpRangesByApp` usage for split. | ✅ |
| `VpnHood.Core.Client` (`VpnHoodClient`, `ClientSessionBuilder`) | Build pipe from descriptor; set `StaticIpFilter.Include = server ∩ device`; **delete `SessionIncludeIpRangesByApp`**. | ✅ |
| `VpnHood.AppLib.App` (`VpnHoodApp`, `LocationService`) | `SplitIpDbManager.EnsureAsync` before connect (app process, iOS memory); emit descriptor instead of ranges. | ✅ |
| `Filtering.Abstractions` | untouched (stays SQLite-free). `StaticIpFilter` unchanged. | — |
| `VpnHood.Core.IpLocations.SqliteProvider` | **untouched** — unrelated (that's country *location* lookup, not used here). | — |

`Microsoft.Data.Sqlite.Core` (not `.Data.Sqlite`) so we pick the native provider per platform:
- Currently: `SQLitePCLRaw.bundle_e_sqlite3` **3.0.3** for all heads (patched natives for every RID; no override).
- Apple heads may later swap to `SQLitePCLRaw.bundle_sqlite3` (system libsqlite3, ships no native binary →
  smallest iOS footprint) — a per-head build-config refinement, not required for correctness or security.
- `SQLitePCL.Batteries_V2.Init()` at startup.

## Streaming raw reader

`.ips` format (per country, already sorted+unified): `[count:int32]` then per range
`[len:byte][bytes][len:byte][bytes]` (len 4 or 16).

```csharp
// no IpRange / IPAddress allocation, no sort; feeds INSERT directly
public static IEnumerable<(byte[] start, byte[] end)> DeserializeRaw(Stream stream)
```
Skips the ~610k×3 object allocations (the real cost) and the sort. Unit-test parity vs `Deserialize`.

## DB schema (`country.db`)

Two tables — v4 as INTEGER (uint32 fits signed 64-bit; fastest + smallest), v6 as 16-byte big-endian BLOB.
Kills IPv4-mapped normalization; each family stays native.

```sql
CREATE TABLE range_v4 (start_ip INTEGER NOT NULL, end_ip INTEGER NOT NULL);
CREATE TABLE range_v6 (start_ip BLOB    NOT NULL, end_ip BLOB    NOT NULL);
CREATE TABLE meta     (key TEXT PRIMARY KEY, value TEXT);
-- indexes created LAST (after all inserts):
CREATE INDEX ix_v4 ON range_v4(start_ip);
CREATE INDEX ix_v6 ON range_v6(start_ip);
```
`meta`: `asset_hash` (note1), `selection_signature` = **sorted country set only** (note2), `schema_version`,
`built_complete`. **note1/note2 live in the `meta` table** (atomic with the data — no sidecar drift).

### DECISION: mode placement — **A (mode runtime)**

The db is a pure cache of country IP ranges (stored as-is, no inversion); a country's ranges are the same
whether you include or exclude it. `mode` is a *user setting* (`UserSettings.SplitCountryMode`), i.e. intent,
not data — so it travels in the descriptor, not the db.

- **A (chosen):** descriptor `{ dbPath, mode }`; db = pure ranges; signature = country set;
  filter configured from args; toggle include↔exclude ⇒ **no rebuild** (same file, different mode).
- **B (rejected):** mode in `meta` ⇒ forced into signature ⇒ rebuild identical bytes on toggle; filter must
  open the db to learn its own mode; conflates intent into a data cache.

Why A: separation of concerns (data vs intent), no needless rebuilds, mode flows straight from its existing
source of truth, filter testable in isolation. B's only edge was one fewer descriptor field — and its
"self-describing db scales across contexts" argument is hollow: **only the country context has a mode**;
`app-ip`/`domain` are include/exclude/block gate sets with no toggle.

## Builder — `SplitIpDbBuilder` (one-shot, app process, RAM ok)

```csharp
static Task BuildAsync(string dbPath, Func<ZipArchive> zip,
    IReadOnlyCollection<string> countries, string assetHash,
    string selectionSignature, CancellationToken ct);
```
1. Build to a **temp file**, then atomic-rename (never mutate a live file). Full rebuild → no stale rows,
   **no country column**, **no deletes, no VACUUM**.
2. `PRAGMA journal_mode=OFF; synchronous=OFF; cache_size=<big>` — disposable build, durability irrelevant.
3. One transaction, prepared `INSERT`, fed by `DeserializeRaw` over each selected country's `.ips`
   (`GetEntry($"{code.ToLower()}.ips")`); v4 rows → `range_v4`, v6 → `range_v6`; batch-commit.
4. `CREATE INDEX` last.
5. Write `meta` incl. `built_complete=1` in the finalizing transaction. Close; `ClearAllPools()`.

Exclude modes store countries **as-is** (no inversion) — filter flips interpretation. `ExcludeMyCountry` = tiny db.

## Change detection — `SplitIpDbManager.EnsureAsync`

Called by app before connect. Open read-only, read `meta`. Rebuild iff:
`built_complete != 1` OR `asset_hash` mismatch OR `selection_signature` (country set) mismatch. Else reuse (µs).
Mode change alone never triggers a rebuild (mode is runtime — decision A).
`asset_hash` = existing `_checksum.txt` MD5 inside the zip. **IncludeAll ⇒ no db** (descriptor path null).
**Static filename** `split-country.db` (see Storage layout) — overwritten in place; no versioning, no orphans.

## Runtime filter — `SqliteIpFilter` (gate)

No dedicated enum — the descriptor reuses `FilterAction`. The filter never returns `Include`
("tunnel" is expressed as `Default` so the outer `StaticIpFilter` gates still apply; Exclude/Block are superior):

```csharp
public sealed class SqliteIpFilter(IIpFilter? next, string dbPath, FilterAction action) : IIpFilter
{
    public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint)
    {
        var r = next?.Process(protocol, endPoint) ?? FilterAction.Default;
        if (r != FilterAction.Default) return r;

        if (action == FilterAction.Default) return FilterAction.Default; // no-op, db never queried

        var member = QueryDb(endPoint.Address);   // v4→range_v4 (long), else range_v6 (blob)
        return action switch {
            FilterAction.Include => member ? FilterAction.Default : FilterAction.Exclude,
            FilterAction.Exclude => member ? FilterAction.Exclude : FilterAction.Default,
            FilterAction.Block   => member ? FilterAction.Block   : FilterAction.Default,
            _ => FilterAction.Default
        };
    }
}
```
Query (µs, single B-tree seek):
```sql
-- v4: SELECT end_ip FROM range_v4 WHERE start_ip <= @a ORDER BY start_ip DESC LIMIT 1;  then @a <= end_ip
-- v6: same on range_v6 with 16-byte blob compare
```
Connection: `Mode=ReadOnly`, `immutable=1` (lock-free concurrent readers), small cache, `PRAGMA mmap_size`,
**one connection + cached prepared stmt per tunnel thread** (`ThreadLocal`). `CachedIpFilter` means each
destination IP hits SQLite ≤ once/hour; steady-state = zero.

**Hot-path (#4) fallback:** if iOS profiling shows first-hit seek hurts, swap internals for an `mmap`'d flat
sorted-range array + binary search (same `IIpFilter`, drop-in). Decide after measuring.

## `ClientOptions` reshape

Drop reliance on giant `IncludeIpRangesByApp` for split. Add per-context descriptor:
```csharp
public string?      SplitIpDbPath { get; set; }   // null ⇒ IncludeAll
public FilterAction SplitIpAction { get; set; }   // Default ⇒ no split; Include/Exclude/Block per db membership
// kept small/in-memory for now (migrate to app-ip.db later):
public IpRange[] BlockIpRangesByApp { get; set; }
public IpRange[] AppIncludeIpRanges { get; set; }   // SplitIpViaApp includes (was folded into country ∩)
public IpRange[] AppExcludeIpRanges { get; set; }
public IpRange[] IncludeIpRangesByDevice { get; set; }  // unchanged
```
Multi-MB cross-process payload gone → path + a few small arrays.

## App-side flow (`VpnHoodApp.GetIncludeIpRanges` / `LocationService`)

1. Resolve `(countries, mode)` from `SplitCountryMode` + `SplitCountries` (ExcludeMyCountry resolves client country first).
2. **Invert to the smaller set** (`LocationService.ResolveSplitIpDbSelection`): the db is mode-independent, so a
   selection and its complement (vs the asset's available codes) express the same split. Always store the strictly
   smaller set and flip Include<->Exclude — "all countries except one" stores one country, not 244. Deterministic
   (no invert on tie); Block is never inverted (it has no complement form). Note: uncovered IPs (in no country's
   ranges) follow the flipped action's default, which matches user intent ("everything except X" tunnels them).
3. `assetHash` = zip checksum; `dbPath` in shared filter-db folder.
4. `await SplitIpDbManager.EnsureAsync(...)` — builds only on change, **in the app process**.
5. Fill descriptor + small app arrays. Stop returning the materialized country `IpRangeOrderedList`.

`GetSupportedSplitCountries` still uses the zip's `GetCountryCodes` (cheap) — no db.
`ClientHelper.GetDnsServers` uses point queries (`IsIncluded`) — works against the composed pipe unchanged;
just call it after the pipe is configured.

## Removals / test impact

- Delete `VpnHoodClient.SessionIncludeIpRangesByApp` (VpnHoodClient.cs:45) — only `ServerNetFilterConfigTest`
  (lines 124-127) reads it. Rewrite that test to assert via composed `IIpFilter.Process(...)` (Include vs Exclude).
- `Src/Apps/Server.Net/appsettings.json` LogLevel Trace→Information (prior task, unrelated) stays uncommitted.

## Storage layout

```
<IDevice.VpnServiceConfigFolder>/ip-filters/split-country.db   ← built now (build temp: split-country.db.tmp)
<IDevice.VpnServiceConfigFolder>/ip-filters/split-app-ip.db    ← later (own gate, symmetric)
<IDevice.VpnServiceConfigFolder>/.../split-domain.db           ← later (own IDomainFilter-side class)
```
`VpnServiceConfigFolder` is the folder the VpnService already reads its config from — reachable by both the
app-builder and the VpnService process (on iOS it is inside the shared app-group container).

**Static filenames — NOT versioned, NOT per country.** `split-country.db` is a *single* file holding the union
of ALL currently-selected countries' ranges (`range_v4`/`range_v6`). Its selection signature + asset hash live
in `meta`, not the filename. Rebuild overwrites the same file in place — **exactly one file, no orphans**.

Why in-place overwrite is safe: **VPN connections are exclusive** — a new connection starts only after the
previous one stops, so nothing holds `split-country.db` open at rebuild time. Build to `split-country.db.tmp`
(also static) → atomic-rename over the target for crash-safety; `built_complete` in `meta` is the backup
crash-detector (open fails / flag missing ⇒ rebuild). No versioned names were needed — that was solving a
file-in-use problem that can't occur here.

## Edge cases / open questions

1. IPv4-mapped: mirror `IpRangeOrderedList.Contains` map logic when normalizing query addresses; v4→range_v4, v6→range_v6.
2. File swap: static `split-country.db` overwritten via temp+atomic-rename. Safe because connections are
   exclusive (nothing holds it open at rebuild). `built_complete` guards against a crashed half-build.
3. Empty IncludeList (0 countries): grants nothing ⇒ all bypass. Confirm vs fallback-to-IncludeAll (current catch falls back on error).
4. Native lib in iOS extension target — measure vs 52 MB budget (bundle_sqlite3 = system libsqlite3, no shipped binary).
5. DNS derivation ordering — after pipe config.
6. Terminal-granter: RESOLVED — `StaticIpFilter` holds ephemeral server∩device; `SqliteIpFilter` lean. See pipe section.
7. Server-not-included Block/Split policy + server-sent block list: DEFERRED, as separate pipe filter(s) — see pipe section.

## Implementation order

1. `DeserializeRaw` streaming reader + parity test.
2. `SplitIpDbBuilder` + `SplitIpDbManager` + tests (build US; build ExcludeMyCountry; rebuild-on-signature-change).
3. `SqliteIpFilter` gate + membership tests (Include/Exclude, v4/v6).
4. `ClientOptions` descriptor + `VpnHoodClient`/`ClientSessionBuilder` wiring; delete `SessionIncludeIpRangesByApp`; fix `ServerNetFilterConfigTest`.
5. `VpnHoodApp`/`LocationService` → `EnsureAsync` + descriptor + shared path.
6. iOS profiling; decide SQLite-immutable vs mmap-array fallback.

Steps 1–3 are self-contained and testable before touching the client.
