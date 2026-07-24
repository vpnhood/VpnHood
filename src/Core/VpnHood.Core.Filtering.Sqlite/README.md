# VpnHood.Core.Filtering.Sqlite

SQLite-backed membership filters. Each answers one question — *which of the stored sets is this
address/domain in?* — from an on-disk database instead of an in-memory list, so sets of any size cost
(almost) no RAM at runtime. Two twins share one build core:

- **Split-ip dbs** — IP range sets, queried per packet through `SqliteIpFilter`.
- **Split-domain dbs** — domain sets, queried per SNI through `SqliteDomainFilter`.

It is context-agnostic infrastructure: it stores sets and knows nothing about where they come from. A
context plugs in by deriving from `SplitIpDbBuilder` / `SplitDomainDbBuilder` (or instantiating the
generic `IpRangeListDbBuilder` / `DomainListDbBuilder`) and supplying two things: a cheap source
signature and the sets. Current consumers: **split-country** (`split-country.db` — its
`SplitCountryDbBuilder`, which owns the ip-location zip layout, lives in AppLib next to
`SplitCountryService`), **split-ip-via-app** (`split-ip-via-app.db`, the user's include/exclude/block
IP files stored as three sets via `IpRangeListDbBuilder`) and **split-domain** (`split-domain.db`, the
user's include/exclude/block domain files stored as three sets via `DomainListDbBuilder`). Each db gets
its own pipe stage. The shared architecture lives in `docs/split-ip/README.md` in the repo root; each
context's policy in its sibling `split-country.md` / `split-ip-via-app.md` / `split-domain.md`.

## Components

| Class | Role |
| --- | --- |
| `SplitDbBuilder` | Abstract base: shared build core (schema, bulk-insert transaction, meta, index-after-insert, atomic replace) + `EnsureAsync` staleness check. |
| `SplitIpDbBuilder` / `SplitDomainDbBuilder` | Schema flavors of the base: bind the ip/domain tables and hand derived classes the matching inserter. Derived classes supply `GetSourceSignature()` and `InsertRangesAsync()` / `InsertDomainsAsync()`. |
| `IpRangeListDbBuilder` / `DomainListDbBuilder` | Generic concrete builders: store plain lists into the per-action sets; every input is a lazy factory. |
| `SplitIpDbInserter` / `SplitDomainDbInserter` | The raw-statement insert hot loops; target a set by action. |
| `SqliteIpFilter` | Runtime `IIpFilter`: read-only per-packet veto gate — the db itself says what membership means. |
| `SqliteDomainFilter` | Runtime `IDomainFilter`: read-only per-SNI gate; its include set is the Include override lane. |
| `SqliteIpFilterChain` / `SqliteDomainFilterChain` | The permanent pipe stage owning the gates: resolves its db paths from a provider, and on `Reconfigure()` swaps the gates (draining in-flight lookups), deletes the superseded db files it had open and raises `Changed`. |
| `SplitDbManifest` | The per-folder source of truth for which dbs are active: atomic write by the app, read by the split filter stages' providers; writing sweeps unlisted db-family files. Presence on disk is never policy — the manifest is. |
| `SplitIpDb` / `SplitDomainDb` | Schema, table naming, key conversion (internal). |
| `SplitDb` | Shared meta table keys/accessors (internal). |
| `SplitSqlite` | One-time `SQLitePCL.Batteries_V2.Init()` (required by `Microsoft.Data.Sqlite.Core`). |

## Database format

One **self-describing** db per selection context, static filename. Three logical sets — include,
exclude, block. All tables always exist; which sets are populated IS the db's semantic, so nothing but
the file path needs to travel.

**Split-ip** (schema version 3): each set split into an IPv4 and an IPv6 table.

```sql
CREATE TABLE include_v4 (start_ip INTEGER NOT NULL, end_ip INTEGER NOT NULL);
CREATE TABLE exclude_v4 (start_ip INTEGER NOT NULL, end_ip INTEGER NOT NULL);
CREATE TABLE block_v4   (start_ip INTEGER NOT NULL, end_ip INTEGER NOT NULL);
CREATE TABLE include_v6 (start_ip BLOB NOT NULL, end_ip BLOB NOT NULL);
CREATE TABLE exclude_v6 (start_ip BLOB NOT NULL, end_ip BLOB NOT NULL);
CREATE TABLE block_v6   (start_ip BLOB NOT NULL, end_ip BLOB NOT NULL);
CREATE TABLE meta       (key TEXT PRIMARY KEY, value TEXT NOT NULL);
-- indexes are created AFTER bulk insert (building the B-tree once beats maintaining it per row)
CREATE INDEX ix_include_v4 ON include_v4(start_ip); -- …and likewise for the other five tables
```

- **IPv4** addresses are stored as unsigned 32-bit values in a positive `long` (fits SQLite's signed
  64-bit INTEGER), so range checks are plain integer comparisons.
- **IPv6** addresses are 16-byte big-endian BLOBs; SQLite's BLOB comparison is memcmp, which matches
  address ordering exactly.
- A set is one logical list spanning both families: a set with only IPv4 rows still constrains IPv6
  addresses (an include set that lists only v4 ranges vetoes every v6 address).

**Split-domain** (schema version 1): one table per set (the `_domains` suffix keeps the names clear of
SQL keywords).

```sql
CREATE TABLE include_domains (domain TEXT NOT NULL);
CREATE TABLE exclude_domains (domain TEXT NOT NULL);
CREATE TABLE block_domains   (domain TEXT NOT NULL);
CREATE TABLE meta            (key TEXT PRIMARY KEY, value TEXT NOT NULL);
CREATE INDEX ix_include_domains ON include_domains(domain); -- …and likewise for the other two
```

- Rows hold the canonical `DomainUtils` form: normalized (trimmed, lower-case, leading `*.` stripped —
  every entry matches itself and its subdomains anyway) and **part-inverted** (`com.google` for
  `google.com`), so an ancestor domain is an ordinal prefix of its subdomains at a `.` boundary.

### Meta keys (rebuild detection)

The db is self-describing — no sidecar files. `SplitDbBuilder.EnsureAsync` opens it read-only and
requires ALL of:

| Key | Meaning |
| --- | --- |
| `schema_version` | Must equal the schema's version (`SplitIpDb` 3 / `SplitDomainDb` 1). |
| `source_signature` | Context-defined identity of the source; mismatch ⇒ source changed ⇒ rebuild. |
| `built_complete` | `"1"` only after indexes exist; a crash mid-build can never be mistaken for a valid db. |

What goes into `source_signature` is the context's choice — the base only compares strings; each
builder's `GetSourceSignature()` composes it so it changes iff the stored sets would change (asset
hash + target set + selection for split-country, source-file mtime + length for split-ip-via-app and
split-domain — see the docs in `docs/split-ip/`). It runs on every ensure, so it must stay cheap:
hashes and stat, never a parse.

Any open/read error also counts as stale (corrupt/locked ⇒ rebuild).

The builder cares only about the file it is asked about: if a db FILE is ever renamed, cleaning up the
old file is the caller's responsibility — the builder never deletes anything beyond its own db (and
its `-wal`/`-shm`/`-journal`/`.tmp` siblings).

## Build strategy

`BuildAsync` is a disposable one-shot that runs in the app process (not the memory-constrained
VpnService):

1. Build into `<dbPath>.tmp`, then atomically `File.Move` over the target. A crash never leaves a
   half-built db in place; VPN connections are exclusive, so the target is never in use.
2. Durability is irrelevant for a rebuildable file: `journal_mode=OFF; synchronous=OFF`.
3. The derived builder streams its rows through `SplitIpDbInserter.Insert(action, start, end)` — raw
   big-endian address bytes, so sources that are already serialized (the `.ips` assets, read via
   `IpRangeOrderedList.DeserializeRaw`) never allocate `IpRange` objects — or
   `SplitDomainDbInserter.Insert(action, invertedDomain)`.
4. The inserters use **raw SQLitePCL statements** (prepared lazily per set, then bind/step/reset per
   row) on the connection's handle. The `SqliteCommand` path costs ~30µs/row on Android (Mono) vs
   ~1µs/row raw — measured on an emulator, an all-countries build (618k rows, 30MB db) dropped from
   16.3s to 0.65s. Domain block lists can reach similar row counts.
5. Meta is written inside the data transaction; `built_complete` only after `CREATE INDEX`.

The insert callbacks (and the factories of the list builders) run only on the rebuild path: the common
case ("already up to date", i.e. every connect after the first) returns at `EnsureAsync`'s meta check
without opening the zip or parsing the text files at all.

## Runtime filter — `SqliteIpFilter`

Chained as an `IIpFilter` pipe stage. It runs its inner `next` first and returns any non-`Default`
result unchanged; otherwise it consults its own sets, in fixed precedence:

| Check (in order) | Verdict |
| --- | --- |
| block set contains the address | `Block` |
| exclude set contains the address | `Exclude` |
| include set is non-empty and does NOT contain the address | `Exclude` |
| otherwise | `Default` ("no objection") |

Like every gate in the pipe it **only vetoes**: `Default` means "no objection" and undecided traffic
tunnels. It never returns `Include` — that value is an explicit override lane (domain force-list, ICMP
force), and an inner `Include` would short-circuit outer gates such as the server∩device allow set.
A non-empty include set vetoes non-members, so chained include sets compose as intersection.

Which sets are populated is probed once at construction (the db is immutable at runtime), so empty
sets cost nothing per packet. Queries are single index seeks (`greatest start_ip <= addr`, then check
its `end_ip`). Each thread gets its own read-only connection with lazily prepared statements
(`Pooling=false`); the surrounding `CachedIpFilter` memoizes per endpoint, so the db sees only
first-contact packets.

## Runtime filter — `SqliteDomainFilter`

Chained as an `IDomainFilter` pipe stage. Unlike the IP gates it consults its **own sets first**
(mirroring `StaticDomainFilter`) and MAY return `Include`: domains are more specific knowledge than
IPs, so the include set is the explicit override lane that forces a domain through the tunnel past any
IP-gate veto (e.g. "tunnel nothing except aaa.com" = include `aaa.com` here + exclude `0.0.0.0/0` in
the via-app IP gate).

| Check (in order) | Verdict |
| --- | --- |
| block set matches the domain | `Block` |
| exclude set matches the domain | `Exclude` |
| include set matches the domain | `Include` (force-tunnel, skips the IP gates) |
| otherwise | inner `next`, else `Default` |

Every entry matches itself and its subdomains, so a lookup probes the inverted domain and each of its
label ancestors (`com.google.www` → `com.google` → `com`) with exact point queries — a single
greatest-prefix seek would miss an ancestor hiding behind a more specific sibling entry (e.g. querying
`www.google.com` against `{google.com, mail.google.com}`). The walk is as deep as the domain has
labels, only on cache miss: the surrounding `CachedDomainFilter` memoizes per domain.

Which sets are populated is probed once at construction; `IsEmpty` lets the client skip enabling SNI
extraction when a db constrains nothing.

## Packaging

Uses `Microsoft.Data.Sqlite.Core` (not `Microsoft.Data.Sqlite`) so each platform head picks its native
SQLite provider and the base stays small. See the comment in the `.csproj` for the
`SQLitePCLRaw.bundle_e_sqlite3` version constraint (GHSA-2m69-gcr7-jv3q) — keep `>= 3.0.3`.
