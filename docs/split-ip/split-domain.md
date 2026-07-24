# Split-domain filtering

Policy of the **split-domain** context (`UseSplitDomain`): how the user's domain filter text files
become `split-domain.db` and what membership means. The shared architecture (filter pipes, storage,
rebuild mechanics) is in [README.md](README.md); the db format is in the
[Filtering.Sqlite README](../../src/Core/VpnHood.Core.Filtering.Sqlite/README.md).

Domains are matched on the **extracted SNI** (TLS ClientHello over TCP, QUIC initial). That makes the
domain gate inherently partial: traffic that carries no readable domain (plain TCP/UDP, raw-IP
connections) returns `Default` from the domain pipe and is decided by the IP gates alone.

## Sources

Three user-edited text files under `<storage>/splits/domains/` (managed by `SplitDomainSettings`,
validated on write, premium-gated by `AppFeature.SplitDomain`):

| File | Meaning when non-empty |
| --- | --- |
| `includes.txt` | These domains are FORCED through the tunnel, past any IP-gate veto. |
| `excludes.txt` | These domains bypass the tunnel (empty ⇒ None). |
| `blocks.txt` | These domains are dropped entirely at app level (empty ⇒ None). |

Entries match themselves and their subdomains (`google.com` matches `mail.google.com`; the `*.` spelling
is accepted and equivalent). Comments (`#`, `;`) and blank lines are ignored by `DomainTextFileParser`.

The on/off gate is `UserSettings.UseSplitDomain`, checked together with the premium feature by the
service itself (it returns null when inactive) — there is no emptiness short path. Missing files count as
empty, and empty sources leave the db's sets empty, which is a no-op gate; the client then skips SNI
extraction entirely (`SqliteDomainFilter.IsEmpty`).

## One db, three sets that mirror the files

`SplitDomainService.EnsureSplitDomainDb` stores each source file into its own set of the single
`split-domain.db`, exactly as written — no merge algebra. Rows are canonicalized (`DomainUtils`:
lower-case, `*.` stripped, parts inverted to `com.google`) so lookups are plain ordinal point queries.

Set semantics differ from the IP dbs in one deliberate way — the include set is NOT a veto:

- **include** — members return `Include`, the explicit override lane: the flow is forced through the
  tunnel and the IP gates are skipped. Non-members pass as `Default` (the set constrains nothing else).
- **exclude** — members bypass the tunnel (`Exclude`).
- **block** — members are dropped entirely (`Block`). Within the db: block > exclude > include.

Why the asymmetry: every packet has an IP, so IP gates can be pure vetoes; but a domain is *more
specific* knowledge than an IP, and an exception to an IP-level exclusion can only be expressed by a
positive override. The canonical composition:

> **Tunnel nothing except `aaa.com`** = `includes.txt: aaa.com` + via-app IP excludes
> `0.0.0.0/0, ::/0`. The domain include wins for `aaa.com`'s TLS/QUIC flows; everything else is
> excluded by the IP gate — including domain-less traffic the domain gate cannot name.

A domain-include veto ("only these domains tunnel") would make that scenario inexpressible while adding
nothing this composition doesn't already cover — and it could never bind domain-less traffic anyway.

Because the include override must not be second-guessed, `SqliteDomainFilter` consults its own sets
BEFORE its inner `next` (mirroring `StaticDomainFilter`) — the opposite composition order of the IP
gates.

## Change detection

`source_signature` = mtime + length of all three source files
(`includes:<ticks>:<len>,excludes:…,blocks:…`). This is stat-only: ordinary connects never read — let
alone parse — the text files; the parse runs only on the rare rebuild path (`DomainListDbBuilder` takes
every set as a *factory*). Every settings write rewrites the file, so the signature always moves with
the content; a spurious touch just costs one fast rebuild.

## Failure policy

Failures propagate and fail the connect (fail-closed): a split the user configured is enforced or the
connection does not proceed, never silently skipped. The user's text files are never modified — they
are the source of truth the user typed in.
