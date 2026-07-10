# VpnHood.Core.Filtering.Sqlite

SQLite-backed IP range membership filter. Answers one question per packet — *is this address in the
stored range set?* — from an on-disk database instead of an in-memory range list, so range sets of any
size cost (almost) no RAM at runtime.

It is context-agnostic infrastructure: it stores IP ranges and knows nothing about where they come
from. A context plugs in by deriving from `SplitIpDbBuilder` (or instantiating the generic
`IpRangeListDbBuilder`) and supplying two things: a cheap source signature and the ranges. Current
consumers: **split-country** (`split-country.db` — its `SplitCountryDbBuilder`, which owns the
ip-location zip layout, lives in AppLib next to `SplitCountryService`) and **split-ip-via-app**
(`split-ip-via-app.db`, the user's merged include/exclude list, plus `split-ip-via-app-blocks.db`, the
block set — both via `IpRangeListDbBuilder`). Each db gets its own pipe stage. The shared architecture
lives in `docs/split-ip/README.md` in the repo root; each context's policy in its sibling
`split-country.md` / `split-ip-via-app.md`.

## Components

| Class | Role |
| --- | --- |
| `SplitIpDbBuilder` | Abstract base: shared build core (schema, bulk-insert transaction, meta, index-after-insert, atomic replace) + `EnsureAsync` staleness check. Derived classes supply `BuildSourceSignature()` and `InsertRangesAsync()`. |
| `IpRangeListDbBuilder` | Generic concrete builder: stores an already-merged `IpRangeOrderedList` as-is; both inputs are lazy factories. |
| `SplitIpDbInserter` | The raw-statement insert hot loop handed to `InsertRangesAsync`; dispatches by address length. |
| `SqliteIpFilter` | Runtime `IIpFilter`: read-only per-packet membership gate driven by a `FilterAction`. |
| `SplitIpDb` | Shared schema, meta keys/accessors, key conversion (internal). |
| `SplitIpSqlite` | One-time `SQLitePCL.Batteries_V2.Init()` (required by `Microsoft.Data.Sqlite.Core`). |

## Database format

One db per selection context, static filename, schema version 2:

```sql
CREATE TABLE range_v4 (start_ip INTEGER NOT NULL, end_ip INTEGER NOT NULL);
CREATE TABLE range_v6 (start_ip BLOB NOT NULL, end_ip BLOB NOT NULL);
CREATE TABLE meta     (key TEXT PRIMARY KEY, value TEXT NOT NULL);
-- indexes are created AFTER bulk insert (building the B-tree once beats maintaining it per row)
CREATE INDEX ix_range_v4 ON range_v4(start_ip);
CREATE INDEX ix_range_v6 ON range_v6(start_ip);
```

- **IPv4** addresses are stored as unsigned 32-bit values in a positive `long` (fits SQLite's signed
  64-bit INTEGER), so range checks are plain integer comparisons.
- **IPv6** addresses are 16-byte big-endian BLOBs; SQLite's BLOB comparison is memcmp, which matches
  address ordering exactly.
- **Ranges are stored as-is (mode-independent).** What membership *means* — tunnel, bypass, block — is
  decided by the `FilterAction` passed to `SqliteIpFilter` at runtime, never baked into the db. A set
  and its complement can therefore express the same split (see the inversion rule in
  `docs/split-ip/split-country.md`).

### Meta keys (rebuild detection)

The db is self-describing — no sidecar files. `SplitIpDbBuilder.EnsureAsync` opens it read-only and
requires ALL of:

| Key | Meaning |
| --- | --- |
| `schema_version` | Must equal the code's `SplitIpDb.SchemaVersion`. |
| `source_signature` | Context-defined identity of the source; mismatch ⇒ source changed ⇒ rebuild. |
| `built_complete` | `"1"` only after indexes exist; a crash mid-build can never be mistaken for a valid db. |

What goes into `source_signature` is the context's choice — the base only compares strings; each
builder's `BuildSourceSignature()` composes it so it changes iff the stored set would change (asset
hash + selection for split-country, source-file mtime + length for split-ip-via-app — see the docs in
`docs/split-ip/`). It runs on every ensure, so it must stay cheap: hashes and stat, never a parse.

Any open/read error also counts as stale (corrupt/locked ⇒ rebuild).

## Build strategy

`BuildAsync` is a disposable one-shot that runs in the app process (not the memory-constrained
VpnService):

1. Build into `<dbPath>.tmp`, then atomically `File.Move` over the target. A crash never leaves a
   half-built db in place; VPN connections are exclusive, so the target is never in use.
2. Durability is irrelevant for a rebuildable file: `journal_mode=OFF; synchronous=OFF`.
3. The derived builder streams its ranges through `SplitIpDbInserter.Insert(start, end)` — raw
   big-endian address bytes, so sources that are already serialized (the `.ips` assets, read via
   `IpRangeOrderedList.DeserializeRaw`) never allocate `IpRange` objects.
4. The inserter uses **raw SQLitePCL statements** (prepare once, bind/step/reset per row) on the
   connection's handle. The `SqliteCommand` path costs ~30µs/row on Android (Mono) vs ~1µs/row raw —
   measured on an emulator, an all-countries build (618k rows, 30MB db) dropped from 16.3s to 0.65s.
5. `_checksum`-style meta is written inside the data transaction; `built_complete` only after
   `CREATE INDEX`.

`InsertRangesAsync` (and the factories of `IpRangeListDbBuilder`) run only on the rebuild path: the
common case ("already up to date", i.e. every connect after the first) returns at `EnsureAsync`'s meta
check without opening the zip or parsing the text files at all.

## Runtime filter — `SqliteIpFilter`

Chained as an `IIpFilter` pipe stage. It runs its inner `next` first and returns any non-`Default`
result unchanged; otherwise it applies the configured action to db membership:

| Configured action | member | not member |
| --- | --- | --- |
| `Default` | `Default` (db never queried) | `Default` |
| `Include` | `Default` | `Exclude` |
| `Exclude` | `Exclude` | `Default` |
| `Block` | `Block` | `Default` |

It **never returns `Include`**: "tunnel" is expressed as `Default` (defer) so outer gates — server
routability, device ranges, block lists — still apply; only the pipe's terminal filter grants
`Include`. `Exclude`/`Block` are superior and short-circuit.

Queries are single index seeks (`greatest start_ip <= addr`, then check its `end_ip`). Each thread gets
its own read-only connection with prepared statements (`Pooling=false`); the surrounding
`CachedIpFilter` memoizes per endpoint, so the db sees only first-contact packets.

## Packaging

Uses `Microsoft.Data.Sqlite.Core` (not `Microsoft.Data.Sqlite`) so each platform head picks its native
SQLite provider and the base stays small. See the comment in the `.csproj` for the
`SQLitePCLRaw.bundle_e_sqlite3` version constraint (GHSA-2m69-gcr7-jv3q) — keep `>= 3.0.3`.
