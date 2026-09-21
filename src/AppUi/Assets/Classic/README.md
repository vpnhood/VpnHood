# VpnHood.AppUi.Assets.Classic

The classic look of the VpnHood app as an inert asset package: the images, country flags, fonts and
content documents a UI draws, and the words of every language, as **one zip** placed beside the
consuming app by the package's targets. No code, no assembly, no dependencies. A brand that wants its
own look ships another package of this shape.

## The rule

**No library may reference this package. Only a head, or the presentation library that chose this
look, may.**

A forker builds their app from the same libraries we do and replaces our artwork and our wording
with theirs. That only works if nothing beneath them is bound to ours. The package has no API, so
nothing can depend on it by accident; the rule is about the reference itself.

## What is in it

| Part | What it is | Who reads it |
|---|---|---|
| `ui.zip` | the store: `images/`, `flags/`, `fonts/`, `content/`, `locales/`, `branding/` - mirrored from the web UI's build into `assets/` and zipped by `_sync-assets.ps1` | `ZipAssetProvider` in `VpnHood.Core.Toolkit`, which extracts it once per version (its hash) under the app's storage |
| `locales/<culture>.json`, `locales/index.json` | the words, one file per language, and the list of languages | `Strings` in `VpnHood.AppUi.Common` |
| `fonts/*.ttf`, `fonts/index.json` | the faces, and the list of them | `AppFontCollection` in the Avalonia UI |
| `branding/<theme>/manifest.json` and the tray icons it names | the look the OS chrome draws with - window and bar colours, tray icons - one per look (`blue`, `violet`) | `AppBranding` in `VpnHood.AppLib.App`, for the theme the head names (`AppOptions.UiTheme`) |
| `buildTransitive/*.targets` | places the zip at `assets/ui.zip` where the consuming app's platform reads files, at any reference depth | the app's build |
| `build/*.targets` | forwards to the above for a direct package reference; the heads of this repo import it | the app's build |

The indexes exist because nothing lists: a provider answers by name, and a page in a browser could
not enumerate. Neither `assets/` nor the zip is in this repo - `.gitignored`, brought in by
`_sync-assets.ps1` from the web UI built beside the repo, which is where it is authored; a git
submodule of its own later. The same script writes `Strings.g.cs` in `VpnHood.AppUi.Common`, one
member per key of `en.json`: the keys, which every store must have words for, are the only thing the
code side takes from this store.

One file, never resources of an assembly: Android packs each assembly once per CPU architecture, so
the same bytes would ship three times; and one file is one item to place per platform and one
request from a browser.

## How it is read at run time

Everything goes through `IAssetProvider` (`VpnHood.Core.Toolkit`): a stream by name, asynchronous.

- Windows, Linux, iOS, tvOS: `FolderAssetProvider` over the placed files opens `assets/ui.zip`;
  Android: `AndroidAssetProvider` copies it out of the package.
- `ZipAssetProvider` extracts it under `<storage>/assets/ui/<hash>/` on the first read and serves its
  entries as files. The head names the zips (`AppOptions.UiZipAssets`, ours one); the app makes ONE
  provider over all of them, which both the app's own UI and its web host read - a second would
  extract the same zip twice. More than one is how a fork replaces single files: they are searched
  in the order given, first hit wins, and each gets its own folder (`ui`, `ui-1`, ...).
- A paired phone's page has nothing placed: the app's web host serves the same entries at `/assets/`,
  and the page reads them through `HttpAssetProvider`, by name, as they are asked for.

## Overriding

Words: implement `IStringSource` and register it with `Strings.AddSourceAsync` - a language we do not
ship, or a few lines said your way, with no build of ours. Bytes: another package of this shape.
