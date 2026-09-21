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

## Making another package of this shape

A brand that wants its own artwork ships its own package and the head references it instead. Four
things decide whether it works, and all four fail silently when they are wrong.

**The id is `<product>.Assets.<source>`** - the kind attaches to the product whose data it is, and
where the data came from comes last (`VpnHood.AppUi.Assets.Classic`,
`VpnHood.Core.IpLocations.Assets.Ip2LocationLite`). That last segment is provenance, not shape: the
zip layout and the entry names are a fixed contract, so the vendor or look that supplied the bytes
is the swappable part. It is the shape every payload package in .NET uses -
`SkiaSharp.NativeAssets.Linux`, `Avalonia.Fonts.Inter`, `Microsoft.NETCore.App.Runtime.win-x64`. A
trailing kind (`Foo.Abstractions`) is for packages with no variant, which is why `....Assets` last
is wrong here.

**Both targets files must be named exactly `<PackageId>.targets`.** NuGet imports
`build/<PackageId>.targets` and `buildTransitive/<PackageId>.targets` by name and by name only.
Rename the package without renaming both files and nothing is imported, nothing is placed, and the
build is green. Verify inside the packed `.nupkg`, not in the source folder.

**`build/` reaches only a direct `PackageReference`; `buildTransitive/` reaches any depth.** Put the
placement in `buildTransitive/` and leave a `build/` file that imports it, as this package does, or
an app that gets here through a library gets nothing. `PrivateAssets="all"` or
`ExcludeAssets="build"` anywhere on the path stops the flow, and a `ProjectReference` never flows
targets at all - which is why the heads in this repo import the targets directly.

**"Is this an app?" is not one property.** The .NET for Android SDK rewrites an app's `OutputType`
from `Exe` to `Library`, so an `OutputType` test alone places nothing on the one platform where the
per-ABI cost matters most. `AndroidApplication == 'true'` is the real discriminator. This one is
found by unzipping the APK, never by a build error.

**Each package owns its own folder** in the consuming app - this one owns `assets/`, the
IP-location package owns `iplocations/` - so two packages never overwrite each other.
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
