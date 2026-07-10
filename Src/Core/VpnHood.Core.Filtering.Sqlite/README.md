# VpnHood.Core.Filtering.Sqlite

SQLite-backed IP range membership filter. Answers one question per packet — *is this address in the
stored range set?* — from an on-disk database instead of an in-memory range list, so range sets of any
size cost (almost) no RAM at runtime.

It is context-agnostic infrastructure: it stores IP ranges, not countries. The split-country feature is
one consumer (see `docs/SplitIpFilter.md` in the repo root for the end-to-end architecture); future
contexts (per-app IP splits, etc.) reuse these same classes with their own db file.

## Components

| Class | Role |
| --- | --- |
| `SplitIpDbBuilder` | One-shot builder: streams serialized ranges from a zip into a new db file. |
| `SplitIpDbManager` | `EnsureAsync` — reuse the db if its meta matches (asset hash + selection), rebuild otherwise. |
| `SqliteIpFilter` | Runtime `IIpFilter`: read-only per-packet membership gate driven by a `FilterAction`. |
| `SplitIpDb` | Shared schema, meta keys/accessors, key conversion, selection signature (internal). |
| `SplitIpSqlite` | One-time `SQLitePCL.Batteries_V2.Init()` (required by `Microsoft.Data.Sqlite.Core`). |

## Database format

One db per selection context, static filename, schema version 1:

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
  and its complement can therefore express the same split (see the inversion rule in the root doc).

### Meta keys (rebuild detection)

The db is self-describing — no sidecar files. `SplitIpDbManager.IsUpToDate` opens it read-only and
requires ALL of:

| Key | Meaning |
| --- | --- |
| `schema_version` | Must equal the code's `SplitIpDb.SchemaVersion`. |
| `asset_hash` | Identifies the source asset build; mismatch ⇒ asset updated ⇒ rebuild. |
| `selection_signature` | Canonical stored set: distinct, upper-cased codes, ordinal-sorted, comma-joined. |
| `built_complete` | `"1"` only after indexes exist; a crash mid-build can never be mistaken for a valid db. |

Any open/read error also counts as stale (corrupt/locked ⇒ rebuild).

## Build strategy

`BuildAsync` is a disposable one-shot that runs in the app process (not the memory-constrained
VpnService):

1. Build into `<dbPath>.tmp`, then atomically `File.Move` over the target. A crash never leaves a
   half-built db in place; VPN connections are exclusive, so the target is never in use.
2. Durability is irrelevant for a rebuildable file: `journal_mode=OFF; synchronous=OFF`.
3. Input ranges stream via `IpRangeOrderedList.DeserializeRaw` — no `IpRange` allocation, no sorting
   (the `.ips` assets are already sorted and unified).
4. The insert hot loop uses **raw SQLitePCL statements** (prepare once, bind/step/reset per row) on the
   connection's handle. The `SqliteCommand` path costs ~30µs/row on Android (Mono) vs ~1µs/row raw —
   measured on an emulator, an all-countries build (618k rows, 30MB db) dropped from 16.8s to 0.65s.
5. `_checksum`-style meta is written inside the data transaction; `built_complete` only after
   `CREATE INDEX`.

`EnsureAsync` takes a `Func<ZipArchive>` rather than an open archive: the common case ("already up to
date", i.e. every connect after the first) returns at the meta check without opening the zip at all.

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
