# VpnHood.AppLib.Assets

VpnHood's own content: the images, country flags, fonts and documents a UI draws, and the words of
every language. It is a **product's** package, not part of the engine and not part of the app.

## The rule

**No library may reference this package. Only a head, or a UI, may.**

A forker builds their app from the same libraries we do, from NuGet, and replaces our artwork and our
wording with theirs. That only works if nothing beneath them is bound to ours. The moment
`VpnHood.AppLib.App` — or anything else a forker must reference — takes a dependency here, our flags
and our sentences become mandatory for everyone.

So the direction is always one way: a head chooses this package, and pushes what it holds *into* the
app through `AppResources` at configure time. The app never reaches out for it.

```
head  ──chooses──►  VpnHood.AppLib.Assets
  │
  └──assigns──►  AppResources.{Icons, Strings, Colors, …}  ──►  VpnHood.AppLib.App
```

`VpnHood.AppLib.App` carries a default for everything it can be handed — the eleven OS-chrome words
in `AppStrings`, the five tray and badge icons — in its own `Resources.resx`. An app that ships no
content package still runs, still speaks, still draws a tray icon. That is the test: if removing this
package breaks a build that has no UI, something below has reached upward.

## What is in it

| Part | What it is | Who reads it |
|---|---|---|
| `assets/` | flags, images, fonts, privacy-consent markdown — **files**, placed per platform by `build/VpnHood.AppLib.Assets.targets` | both UIs, by path; the web server serves the same folder to the SPA over `/assets/…` |
| `assets/icons/` | the five tray and badge icons, in the shape a head hands to `AppResources.Icons` — a sample a fork replaces with its own | nothing reads them; they are here to be copied, and are the only files under `assets/` this repo keeps |
| `Locales/*.json` | one file per language, **embedded** so text needs no folder | `Strings`; the web server answers the SPA's `/assets/locales/{code}.json` from here |
| `Strings`, `Strings.g.cs` | the generated accessor over those locales | the Classic UI; today also `Api.WebHost` and `Android.Common` — see below |
| `AppContent` | locates the content folder — `FolderName`, `FolderPath`, `FolderResolver` | the platform packages that place the folder, and the UIs that read from it |
| `Mdi`, `AppFonts`, `Markdown`, `ErrorActions`, `ErrorContext`, `ErrorMessages` | glyph names, font registration, markdown rendering, error presentation | `VpnHood.AppUi.Presentation.Classic.Avalonia` only |

Files are never embedded in the assembly: Android packs each assembly once per CPU architecture, so
the same bytes would ship three times. The words *are* embedded, because text has to work with no
folder named — a service, a CLI, a page in a browser.

## Known exceptions, to be closed

Two libraries still reference this package, and both are the kind of coupling the rule forbids:

- `VpnHood.AppLib.Api.WebHost` — `Strings.OpenLocaleFile`, to answer the SPA's locale request.
- `VpnHood.AppLib.Android.Common` — `Strings.*` for its notification and quick-tile labels, which
  `AppResources.Strings` already carries as the head-overridable `AppStrings`.

Neither is a UI, so a forker who wants neither our wording nor our artwork still gets both. Closing
them means routing those reads through `AppResources` and having the head supply the package.

## Overriding

A product replaces any of it by assigning at configure time — `ConfigParams.Strings`, or
`AppResources.Icons.SystemTrayConnectedIconData`. Whatever the head assigns wins; whatever it leaves
alone falls back to the library's own copy. There is no folder the app searches behind your back.
