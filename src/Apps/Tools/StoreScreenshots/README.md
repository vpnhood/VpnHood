# Store screenshots

Every picture a store listing shows, drawn by the app's own UI with no app, no engine, no VPN and
no display. Written for a maintainer with zero context, human or otherwise: read this before
changing anything here, and update it in the same change when the shape of the thing changes.

## What it does

The Avalonia UI (`VpnHood.AppUi.Presentation.Classic.Avalonia`) is started headless against a
**fixture** — a recording of what the app's API answers, with every identifying value replaced by
synthetic data — one page is opened, and the frame is captured at a device's exact pixel size. A
second pass draws the device around that capture (a phone mockup, a desktop window, or nothing at
all) and writes the file the store takes. A third copies the finished sets into the store repo's
fastlane trees.

Nothing here runs the app: the pages read `VhApp`, and `VhApp` is handed fakes of the six API
interfaces answering from the fixture. That is what makes a run reproducible on a CI box with no
credentials.

## Commands

```
VpnHoodStoreScreenshots generate --config <store/screenshots.json> --assets <store folder or ui.zip>
    [--out <folder>] [--platform ios,android-phone] [--device ipad-13] [--locale fa] [--only 1,3]
    [--jobs 8] [--capture-only | --frame-only] [--install] [--install-root <folder>]

VpnHoodStoreScreenshots shot  --fixture <one shot's json> --assets <store> --route /protocols
    --width 360 --height 692 --scale 8 --culture en --out <png> [--hide A/B] [--freeze flasher]

VpnHoodStoreScreenshots frame --device <device json> --assets <store> --in <png> --out <png>
```

`generate` is the one to run; it spawns `shot` and `frame` itself, one process per picture, several
at a time. The other two exist because that is how a wrong pixel is chased: every run keeps the
exact input of every picture, so one of them can be drawn again by hand.

`--assets` is the UI's asset store: the folder `src/AppUi/VpnHood.AppUi.Assets.Classic/_sync-assets.ps1` mirrors,
or the `ui.zip` a shipped head carries. The UI has no pictures, faces or words of its own.

## The files an app owns

| file | what it is |
|---|---|
| `store/screenshots.json` | languages, stores, devices, screens, install destinations. THIS is what a fork edits; the tool is never edited to change a listing. JSON with `//` comments. |
| `store/fixture.json` | the app's own answers, recorded from a Release client. Never real keys, IPs or accounts. |
| `store/fixture.ios.json` | a patch too long to read inline — Connect's premium-only location list, derived from its fixture by the app's own rules. |

## Invariants — do not break these

1. **The fixture says which product it is.** `features.uiTheme`, `logoAssetPath`,
   `privacyConsentAssetName`, `privacyPolicyUrl` and `termsOfUseUrl` are refused rather than filled:
   a picture wearing the other product's theme or logo looks right and is wrong. Copy them from the
   product's own head (`src/Apps/<product>/<head>/AppConfigs.cs`, or `src/Apps/Tools/AvaloniaUI.Dev`
   for a whole product at once).
2. **A capability patch is never invented.** Each value mirrors a device class in this repo and is
   cited beside it in the configuration. When a capability changes there, it changes there too.
3. **Array order is store order.** A file is numbered by where its shot sits in the list, so
   reordering a set means moving items, never renumbering them.
4. **Fail loud.** A route with no page, a hide path that matches nothing, a language the store has
   no words for, a dialog over the page, an error the UI logged, a missing final at install time —
   each one fails the run. A silent fallback would ship a broken listing.
5. **Numbers in a patch have the app's own type.** The fixture goes through the typed API, so a
   value for an integer field must be whole. `82.4 * 100_000` is not an integer in JavaScript, which
   is what the retired engine used to write.

## What a screenshot must not contain

- **A dialog, a snackbar or an update notice.** Each one is an error the page caught and showed, and
  the run fails rather than shipping it.
- **An animation caught mid-stroke.** The UI's pulses and spinners run forever, so the same picture
  would differ every run. The classes are taken off before the capture (`--freeze`, default
  `flasher`) — the toolkit's reduced motion. Avalonia 12 keeps its animation clock internal, so
  there is nothing to pause instead.
- **Another company's artwork.** The Split Apps screen lists well-known app names over plain tiles
  in the colour each is known by, drawn here; a real icon is its owner's artwork and trademark.
- **A caution aimed at someone who opted in.** The IP-leak chip on the Split Tunneling screen is
  true in the app and reads as a claim about the product on a store page, so that shot hides it by
  control name.

## Determinism

The same inputs give the same bytes: three runs of the text-heavy Protocols screen and of Connect's
location list were byte-identical. Skia in software, no browser compositor. This matters because the
listing publish is gated on a fingerprint of what it would send — a picture that re-renders
differently is re-uploaded to every store on every run.

A run may still log `the page kept changing`: that is the settle wait failing to *prove* stillness
within its timeout, not proof of movement. The bytes are stable; the message says the tool could not
confirm it.

## Where the rest of the pipeline is

Screenshots are one of six things a listing needs. The others — the translated store texts, the
release notes, the publish-state gate, the App Store screenshot sync, the icon alpha check and the
subscription texts — are still JavaScript tools in `vpnhood/VpnHood.AppUi.Spa`, called by the store
repos' workflows, and are described by that repo's `e2e/store/README.md`. They have to move here
too before that repo can be archived.

The asset store is still built from that repo as well (`_sync-assets.ps1` reads its `dist`), until
the assets become a module of their own.
