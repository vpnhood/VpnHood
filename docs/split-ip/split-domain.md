# Split-domain filtering

Policy of the **split-domain** context (`SplitTunneling.UseDomain`): how the user's domain filter text files
become `split-domain.db` and what membership means. The shared architecture (filter pipes, storage,
rebuild mechanics) is in [README.md](README.md); the db format is in the
[Filtering.Sqlite README](../../src/libs/Core/VpnHood.Core.Filtering.Sqlite/README.md).

Domains are matched on the **extracted SNI** (TLS ClientHello over TCP, QUIC initial). That makes the
domain gate inherently partial: traffic that carries no readable domain (plain TCP/UDP, raw-IP
connections) returns `Default` from the domain pipe and is decided by the IP gates alone. A TCP connection
whose app sends nothing in the first second is one of them: where the server speaks first, as with SMTP,
FTP or MySQL, the app waits for it, and the server is reached only after the verdict. A ClientHello begun
and not complete within that second fails the connection rather than pass it unnamed, as does one that is
not well formed. A name that Encrypted Client Hello hides is not read either: the rules see the outer
name, the provider's.

## Sources

Three user-edited text files under `<storage>/splits/domains/` (managed by `SplitDomainSettings`,
validated on write, premium-gated by `AppFeature.SplitDomain`):

| File | Meaning when non-empty |
| --- | --- |
| `includes.txt` | These domains are FORCED through the tunnel, past any IP-gate veto. |
| `excludes.txt` | These domains bypass the tunnel (empty ⇒ None). |
| `blocks.txt` | These domains are dropped entirely at app level (empty ⇒ None). |

Entries match themselves and their subdomains (`google.com` matches `mail.google.com`), so an entry takes
no `*`: a list with one is refused when it is saved (`SplitEntryException`, which the UI words with the
entry), and a list stored before is read without an entry's leading `*.`, which meant the same
(`SplitFileSettings.Normalize`). Comments (`#`, `;`) and blank lines are ignored by `DomainTextFileParser`.

The on/off gate is `UserSettings.SplitTunneling.UseDomain`, checked together with the premium feature by the
service itself (it returns null when inactive) — there is no emptiness short path. Missing files count as
empty, and empty sources leave the db's sets empty, which is a no-op gate; the client then skips SNI
extraction entirely (`SqliteDomainFilter.IsEmpty`).

## One db, three sets that mirror the files

`SplitDomainService.EnsureSplitDomainDb` stores each source file into its own set of the single
`split-domain.db`, exactly as written — no merge algebra. Rows are canonicalized (`DomainUtils`:
lower-case, `*.` stripped, a final dot dropped, parts inverted to `com.google`) so lookups are plain
ordinal point queries; the names looked up are normalized the same way, so `mail.google.com.` is
`mail.google.com`.

Set semantics differ from the IP dbs in one deliberate way — the include set is NOT a veto:

- **include** — members return `Include`, the explicit override lane: the flow is forced through the
  tunnel and the IP gates are skipped. Non-members pass as `Default` (the set constrains nothing else).
- **exclude** — members bypass the tunnel (`Exclude`).
- **block** — members are dropped entirely (`Block`).

Within the db the most specific entry that matches decides, whichever set holds it, so an entry under
another is an exception to it: `google.com` excluded with `mail.google.com` included sends mail through
the tunnel and the rest of Google around it. Only between entries of the same domain is the order
block > exclude > include.

A flow whose name an entry matches takes that verdict whatever the IP gates say, TCP and QUIC alike: an
include goes through the tunnel past an IP exclude or block, and an exclude goes around it past an IP
block. The IP gates decide only a flow whose name matches no entry, or that has none. A TCP connection is
decided once, when its name is read, so a rule that changes while connected, IP or domain, ends the open
TCP connections, and one still being set up is not joined to its host or to the tunnel; the apps' new ones
follow it (`ClientSession`). So does the change that takes the last domain rule away. IPv6 that the server
or the device's own network does not carry is apart: its TCP packets meet the IP gates first, as with no
domain rule on, so a connection that cannot go the way its verdict says, into the tunnel or around it, is
never made, and the app goes on to IPv4 (`ClientPacketHandler`).

A QUIC flow is the packets between two addresses and ports, and a client that keeps one socket may run
several connections on it to one server address, as to a CDN's, at once or one after another. Each has
its name read from its first packets. A blocked one is dropped there and never gets past them, so the
others go on. Past the first packets the connections of a flow are not told apart, so the flow goes by
the most protective of their verdicts: include, then no entry, then exclude. None leaves the tunnel by
another's exclude, at the price of an excluded name that is not excluded while it shares a flow with one
that is not. A connection's first packets still go by its own verdict, so the IP gates judge a name with
no entry there, whatever the flow's verdict. Where they send it another way than the flow goes, as an IP
exclude does under an included flow, that connection may fail: its later packets follow the flow.

Why the asymmetry: every packet has an IP, so IP gates can be pure vetoes; but a domain is *more
specific* knowledge than an IP, and an exception to an IP-level exclusion can only be expressed by a
positive override. The canonical composition:

> **Tunnel nothing except `aaa.com`** = `includes.txt: aaa.com` + via-app IP excludes
> `0.0.0.0/0, ::/0`. The domain include wins for `aaa.com`'s TLS/QUIC flows; everything else is
> excluded by the IP gate — including domain-less traffic the domain gate cannot name — except what
> the client forces through the tunnel over an exclusion: DNS on ports 53 and 853 under the default
> `SplitDnsMode.IncludeAll` (`DefaultRoute` lets it follow the IP gate), and pings (ICMP echo). A block
> still drops either.

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
