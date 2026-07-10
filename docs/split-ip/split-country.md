# Split-country filtering

Policy of the **split-country** context: which countries' IP ranges go into `split-country.db` and
what membership means. The shared architecture (descriptors, filter pipe, storage, rebuild mechanics)
is in [README.md](README.md); the db format is in the
[Filtering.Sqlite README](../../Src/Core/VpnHood.Core.Filtering.Sqlite/README.md).

## Selection

`SplitCountryService.EnsureSplitIpDb` maps the user's `SplitCountryMode` to a country set and a
`FilterAction`:

| SplitCountryMode | Countries | Action |
| --- | --- | --- |
| `IncludeAll` | — | `Default` (no db, filter not created) |
| `IncludeList` | `UserSettings.SplitCountries` | `Include` |
| `ExcludeList` | `UserSettings.SplitCountries` | `Exclude` |
| `ExcludeMyCountry` | client country (fresh lookup, no cache/server hint) | `Exclude` |

The country ranges never enter memory — `SplitCountryDbBuilder` streams them from the ip-location zip
into SQLite. The builder lives next to the service (not in `Filtering.Sqlite`): the zip layout and the
asset-hash signature are country business, the sqlite project stays context-agnostic.

Failures propagate and fail the connect (fail-closed): connecting with the split silently not applied
could route traffic the user meant to keep out of the tunnel — for `ExcludeMyCountry` that can be
safety-critical.

## The inversion rule (smaller-set short path)

The db stores ranges as-is; the action gives them meaning. A selection and its complement (against the
asset's available countries) therefore express the same split, so
`SplitCountryService.ResolveSplitIpDbSelection` always stores the **strictly smaller** set and flips
`Include`↔`Exclude` to match. "All countries except one" — whether the UI sends 243 includes or one
exclude — stores one country and builds in milliseconds. Ties don't invert (deterministic); selected
codes unknown to the asset are dropped before comparing; `Block` is never inverted (no complement form).

Consequence: addresses covered by *no* country in the asset (unallocated space) follow the flipped
action's default. For "everything except X" they are tunneled, which matches user intent.

This rule is country-specific: the complement is cheap because it means streaming fewer country
*files*. Contexts that store arbitrary range lists (split-ip-via-app) gain nothing from inversion.

## Change detection

`source_signature` = asset hash + the stored selection (distinct, upper-cased, ordinal-sorted codes).
The asset hash is the zip's `_checksum.txt` (stamped at asset build time), falling back to an MD5 of
the zip bytes. Ordinary connects hit the signature check and never open the zip. The mode is
intentionally NOT part of the signature — the db content is mode-independent (see the inversion rule).

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
