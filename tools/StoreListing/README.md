# Store listing — maintainer's map

This document is the entry point for maintaining the store listing pipeline. It is written for a
maintainer with **zero context** — human or AI — because the code was built and is maintained
without line-by-line review. Read this before changing anything; update it when the shape of the
system changes. Detailed *why* lives as comments at the top of each file named below — this file
tells you which file owns what, which invariants must hold, and how to verify a change without
trusting anyone's memory.

## The system in one paragraph

The **main repo** (`vpnhood/VpnHood`) owns every tool and every *publish action*: binaries, GitHub
releases, the shared listing workflow the store repos call, the tools in this folder, and the
screenshot tool beside it ([`../StoreScreenshots`](../StoreScreenshots/README.md)). The **store-asset
repos** (`vpnhood/Vpnhood.App.Client`, `vpnhood/Vpnhood.App.Connect`) own the *content*: translatable
source texts, generated screenshots, compiled fastlane trees — no app source. Nothing ever writes
across repo boundaries, which is what keeps the whole pipeline free of PATs: a reusable workflow runs
in its **caller's** context, so when a store repo calls main's workflow, checkouts and commits land
on the store repo with its own token.

## Data flow

```
store-i18n/en-US/store.json     hand-written EN source texts   (store repo)
store-i18n/en-US/subscriptions.json   hand-written EN subscription texts (store repo)
        │ vhtranslator (Gemini) → 12 sibling locales (both files, same run)
        ▼
store-metadata.mjs              compiles texts → fastlane/metadata/{android,ios}/<locale>/*.txt
StoreScreenshots                draws the Avalonia UI against the store repo's store/screenshots.json
                                and store/fixture.json → fastlane screenshots/images
store-subscriptions.mjs         pushes subscription texts DIRECT to App Store Connect (no fastlane
                                path exists for in-app-purchase localizations), run by hand
        │  the first two run in update-screenshots.yml (store repo), which commits the results
        ▼
publish_listing.yml (stub, store repo) → publish_listing.yml (main, reusable)
        ├─ gate: store-publish-state.mjs   fingerprint vs fastlane/publish-state.json
        ├─ _publish_listing_appstore.yml   deliver (text) + store-asc-screenshots.mjs (images)
        ├─ _publish_listing_play.yml       fastlane supply (text + checksum-synced images)
        └─ record: commits updated fingerprints back to the store repo
```

Binaries are a separate, parallel path: `publish_client.yml` / `connect_publish.yml` →
`publish_app.yml` → build modules → TestFlight / Play / GitHub release. A release uploads the
binary and its changelog and **never** the listing (see the `playstore` lane comments).

**Release notes** ("What's New") have their own source — the monorepo's hand-maintained
CHANGELOG.md — and their own refresh workflow, but ride the same translation and shipping rails:

```
vpnhood/VpnHood CHANGELOG.md    first H1 section ("# Latest"), hand-written EN, line tags:
        │                       #client/#connect (product) · #android #ios #windows #linux
        │                       (platforms, inclusive; none = all) · #store (Play's short note)
        ▼ store-release-notes.mjs extract     runs in update-release-notes.yml (store repo)
store-i18n/en-US/release-notes.json   one key per line (content-hashed: an edited line is
store-i18n/release-notes.map.json     retranslated, the rest reused) + order/routing map
        │ vhtranslator (same run) → sibling locales, changed lines only
        ▼ store-release-notes.mjs compile
fastlane/metadata/ios/<locale>/release_notes.txt          ships with the LISTING publish
fastlane/metadata/android/<locale>/changelogs/default.txt ships with the RELEASE (any versionCode)
```

## The tools

All of them use Node built-ins only, so no workflow installs anything, and a workflow that needs
them checks out this one folder. Every one takes the store repo as `--root`; a workflow passes
`$GITHUB_WORKSPACE`.

| file | contract |
|---|---|
| `store-metadata.mjs` | Compiles `store-i18n` texts into fastlane trees. Fails loud on limits/missing keys/platform names in iOS copy (Guideline 2.3.10). `--check` validates without writing. |
| `store-release-notes.mjs` | Release notes from the monorepo CHANGELOG: `extract` (first H1 section → per-line store-i18n source + routing map) and `compile` (translations → iOS `release_notes.txt`, cap 4000, 2.3.10-linted; Play `changelogs/default.txt` from the `#store` lines, cap 500). `--product client\|connect` picks the `#client`/`#connect` lines, and `--no-ios` deletes the iOS files (Apple rejects a FIRST App Store version that carries "What's New"). Missing map = not adopted, warn + skip; anything else wrong = red. |
| `store-publish-state.mjs` | Fingerprints what a publish would send; compares with `fastlane/publish-state.json` in the store repo. Text hashed as LF (CRLF checkouts must fingerprint identically to CI). Record only after a **verified** publish. |
| `store-asc-screenshots.mjs` | Checksum-sync of App Store screenshots (the Play `sync_image_upload` equivalent). Replaces deliver's delete-all mode. Phased against Apple's ghost-delete 500s — the file header documents the observed evidence. `--check` reports drift. |
| `store-icon.mjs` | Strips alpha from iOS appiconsets (ITMS-90717). Refuses genuinely translucent pixels. Run by hand. |
| `store-subscriptions.mjs` | Pushes subscription texts (group name, per-product name + description) from `store-i18n/*/subscriptions.json` straight to App Store Connect. Not part of the listing publish: fastlane has no in-app-purchase localization path, so `deliver` cannot carry these and the API is the only route. Keys are product ids, so a fork edits the JSON and no code — same choice `asc-iap.mjs` makes with `--products`. Enforces Apple's limits (name 30, description 45) before sending, and prints each product's state afterwards because editing a `READY_TO_SUBMIT` product can move it. Idempotent; `--check` reports drift. Play needs no equivalent — Google takes subscription texts from the Play Console only. |

## Invariants — do not break these

1. **Generated files are never hand-edited**: fastlane trees come from the compiler and the
   screenshot tool; `store-i18n/<non-en>/` comes from vhtranslator; only `store-i18n/en-US/store.json`
   and `store-i18n/en-US/subscriptions.json` are written by hand. `store-i18n/en-US/release-notes.json`
   and `release-notes.map.json` are generated too (from the monorepo CHANGELOG — the hand-written
   surface is the CHANGELOG's `# Latest` section). Hand-edits are silently overwritten by the next
   generation run.
2. **No repo writes to another repo.** If a change seems to need that, the design is wrong or a
   deliberate credential decision is being made (see [`docs/cicd/deployment.md`](../../../../docs/cicd/deployment.md)).
3. **`publish-state.json` records only verified publishes.** A cancelled or failed store leg must
   not be recorded — otherwise the gate skips a store that never got the content. The workflow's
   `record` job enforces this via each module's `published` output.
4. **Fail loud.** Every tool throws on missing files, unknown locales, limit overflows. Never add
   a silent fallback; VpnHood is fail-closed by policy.
5. **Missing credential = warn + skip + green; present-but-failing = red.** The fork-friendliness
   convention, documented per-secret in [`docs/cicd/deployment.md`](../../../../docs/cicd/deployment.md).
6. **iOS copy never names other platforms** (Android/Google Play/Windows/Linux) — App Store
   Guideline 2.3.10. The compiler lints texts; screenshots must respect it too (the Servers page
   promo was removed on iOS for this — don't reintroduce it).

## Apple lore (hard-won, with evidence)

- **Screenshot replace = delete + create of the same fileName.** Apple's deletes are eventually
  consistent; a create that reuses a just-deleted name can 500 **and keeps 500ing while retried**
  (observed >15 min), yet succeeds when tried once later. Hence the phased sync. Never reintroduce
  a delete-everything re-upload (deliver's `overwrite_screenshots`) into CI.
- **A cancelled screenshot push scrambles display order** (uploads land in completion order; the
  ordering PATCH runs last). `store-asc-screenshots.mjs` restores order even on failed runs.
- **First-version quirks**: v1 has no "What's New" (`release_notes.txt` must not exist for it —
  `--no-ios` in the store repo's workflows keeps it deleted); deliver's first-version text push
  raises a benign "No data" (rescued in each store repo's Fastfile `upload_metadata` lane —
  removable once 1.0 is live).
- **"What's New" is never carried over to a new version** — the field starts empty and is required
  per localization once the app has a prior release. The fingerprint gate compares only repo
  content, so before honoring a skip it probes the live listing
  (`store-asc-screenshots.mjs --whatsnew-check`, exit 4 = empty fields to fill) and pushes the
  otherwise-unchanged text to fill them. No manual `force=true` needed for this case.
- **The listing locks while a version is in review or live with none open.** That is a normal
  state between releases: the appstore module probes it (`--editable-check`, exit 3 = locked) and
  warns + skips with `published=false`, so the gate re-publishes once a version opens. A locale on
  the listing without local screenshots is likewise a warn-and-skip (staged rollout — text ships
  before screenshots; the storefront falls back to the primary locale's images), while local
  screenshots for a locale the listing lacks stay a hard error.
- **ASC API**: JWT ES256, `exp` ≤ 20 min (Apple rejects at the cap; use 15). Subtitle and privacy
  URL live on `appInfoLocalizations`; description/keywords/promo/URLs on
  `appStoreVersionLocalizations`. `sourceFileChecksum` is the MD5 of the uploaded file — the basis
  of the sync diff.
- **Review submissions are a basket (`reviewSubmissions`) whose contents are near-opaque.** Items
  list with **empty relationships**, and `GET /v1/reviewSubmissionItems/<id>` is **403**, so the only
  way to know what is in a basket is to base64-decode the item id: it is
  `<submissionId>|<typeCode>|<targetId>`. Type codes seen so far — `6` appStoreVersion, `18`
  subscriptionVersion, `19` subscriptionGroupVersion.
- **To add a subscription to a submission, add its VERSION, not the product.** `reviewSubmissionItems`
  has no `subscription` relationship — that returns `409 ENTITY_ERROR.RELATIONSHIP.UNKNOWN`, which
  reads like a key/role problem and is not, and cost two rounds of chasing permissions. The
  relationship is `subscriptionVersion`, taking an id from `/v1/subscriptions/<id>/versions`
  (each product has one pending version, `PREPARE_FOR_SUBMISSION`):

  ```jsonc
  { "data": { "type": "reviewSubmissionItems", "relationships": {
      "reviewSubmission":    { "data": { "type": "reviewSubmissions",    "id": "<submission id>" } },
      "subscriptionVersion": { "data": { "type": "subscriptionVersions", "id": "<version id>" } } } } }
  ```
- **`store-subscriptions.mjs` has a submission side effect.** Writing group-level text creates a
  pending *subscription group version* that joins the open review submission **by itself**; the
  products do not follow. Submitting in that state fails with *"New subscription groups must be
  submitted with an auto-renewable subscription from within that group"* — a new group with nothing
  buyable in it. After any group-localization change on an unreleased group, add both product
  versions to the basket before submitting.
- **A REJECTED version is still editable — the tooling used to disagree.** Apple reopens a version
  for editing after App Review rejects it, which is exactly when corrected metadata has to go up.
  `--editable-check` originally accepted only `PREPARE_FOR_SUBMISSION`, so a rejection made the gate
  report "locked", skip the whole App Store leg, and finish **green** — a fix that looks published
  and is not. The editable set is now `PREPARE_FOR_SUBMISSION`, `REJECTED`, `DEVELOPER_REJECTED`,
  `METADATA_REJECTED`, `INVALID_BINARY`. Only in-review and live-with-no-open-version are locked.
- **App icons must have no alpha channel** (ITMS-90717) — rejected on channel presence, not
  transparency. Applies to every size in the appiconset, app and extension both.

## How to verify (never trust, always run)

From the main repo's root, with the store repos beside it:

```bash
# texts compile clean and within store limits
node src/Apps/Tools/StoreListing/store-metadata.mjs --check --root ../Vpnhood.App.Client

# what would a publish send vs. what was last published?
node src/Apps/Tools/StoreListing/store-publish-state.mjs --root ../Vpnhood.App.Client

# is the LIVE App Store listing exactly the repo content? (counts, checksums, order)
node src/Apps/Tools/StoreListing/store-asc-screenshots.mjs --bundle-id com.vpnhood.client.ios \
  --root ../Vpnhood.App.Client --keys-dir <your keys> --check

# the same for the Apple TV's version: add --platform TV_OS (reads fastlane/screenshots/appletv);
# without --check it syncs them. publish_listing.yml has no Apple TV pass yet.

# full pipeline, CI: dispatch and watch (vh-ref = the VpnHood ref the shipped app was built from,
# whose Avalonia UI draws the screens and whose tools compile the texts)
gh workflow run publish_listing.yml -R vpnhood/Vpnhood.App.Client
gh workflow run update-screenshots.yml -R vpnhood/Vpnhood.App.Client -f vh-ref=develop
```

In CI (`update-screenshots.yml` in each store repo) the `generate` job checks out `vpnhood/VpnHood`
at `vh-ref` with its asset store and draws one language per runner; the `assemble` job installs the
sets, then runs `store-metadata.mjs` and `store-release-notes.mjs` from the same ref. `publish_listing.yml`
takes the tools from `tools_ref` (default `develop`).

The Gemini key is the org secret `GOOGLE_GEMINI_TRNSLATE_APP_API_KEY` (one key for the app's words
and both store listings). Workflows hand it to the tool as `GEMINI_API_KEY`, which is what
vhtranslator reads — the two names differ on purpose.

An unchanged listing must yield: gate green, both store jobs **skipped**, no record commit. A
forced run (`-f force=true`) must yield all jobs green with the App Store leg ~1 minute.

## Recipes

- **Add a locale**: add to `store-i18n/locales.json` (with per-store overrides if a store's code
  differs; `null` = store lacks it), to vhtranslator's config and to the `locales` of
  `store/screenshots.json` in each store repo. Run update-screenshots.yml; vhtranslator fills the
  texts.
- **Change a screenshot page/order**: edit `store/screenshots.json` in the store repo (array order IS
  store order; [`../StoreScreenshots`](../StoreScreenshots/README.md) has the rest). Regenerate; the
  listing publish syncs the delta.
- **Change store texts**: edit `store-i18n/en-US/store.json` in the store repo only; CI
  translates, compiles, commits; the gate picks up the change on the next listing publish.
- **Update release notes for a release**: edit the monorepo CHANGELOG's `# Latest` section
  (tags: `#client`/`#connect`, `#android #ios #windows #linux`, `#store` for Play's 500-char
  note — all trailing, per line), then run `update-release-notes.yml` in each store repo. iOS
  notes ship with the next listing publish; the Play note ships with the next release's AAB.
  When an app's FIRST App Store version goes live, drop `--no-ios` from its workflows.
- **Add a store (e.g. Microsoft)**: new `_publish_listing_<store>.yml` module in main following
  the appstore/play pattern (optional credential → warn+skip; `published` output), a `STORES`
  entry in `store-publish-state.mjs`, a compiler section in `store-metadata.mjs`. fastlane has no
  MS support — use msstore CLI/StoreBroker; note MS couples packages+listing in one submission.

## Debugging a wedged App Store push

Do **not** blind-rerun or cancel first. `deliver`'s "Waiting for screenshots to appear …
error=Server error got 500" means the *post-upload verification poll* is failing — the uploads
themselves usually landed. Query the truth via the ASC API (see `--check` above): find sets with a
missing/stuck file, fix surgically (the sync tool does this), restore order, and only record the
publish after verification passes.
