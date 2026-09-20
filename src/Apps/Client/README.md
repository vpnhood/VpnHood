# VpnHood.App.Client

The configuration our own products share — VpnHood Client and VpnHood Connect, across Android, iOS,
Windows and Linux. It is **not** a library, it is not published, and a forker does not use it.

## What it is for

Every head in `src/Apps/` is a thin platform shell: an activity, a view controller, a WPF window. The
decisions that are the same for all of them live here, so they are made once and made identically:

| File | What it decides |
|---|---|
| `IRequiredAppConfigs.cs` | the settings each product must state — `AppId`, `WebUiPort`, `UpdateInfoUrl`, `DefaultAccessKey`, `Ga4MeasurementId`, `RemoteSettingsUrl`, `PrivacyPolicyUrl`, `TermsOfUseUrl` … Each head implements it, so a new product cannot forget one. |
| `AppWebRoot.cs` | the page every web host of both products serves: the Avalonia browser build this assembly embeds |
| `ConnectAppResources.cs` | VpnHood Connect's premium feature list |
| `EmbeddedResource.cs` | reads an optional embedded blob, returning null when the build did not embed one |
| the project file | pins the SPA package (`VpnHood.AppLib.Assets.ClassicSpa`), the IP-location database, and the local-SPA switch |

## Why a forker does not reference it

This is where *our* answers live: our app ids, our measurement ids, our access keys, our branding, our
SPA version. A fork wants its own of each. The shape that makes that possible is one layer down —
`AppOptions`, `AppResources`, the provider interfaces in `VpnHood.AppLib.Abstractions` — all of which
ship on NuGet and know nothing about this project.

So a fork writes its own equivalent of this folder: twenty lines that fill in `AppOptions` and hand
over its own resources. That is the whole integration surface, and it is the reason nothing beneath
`src/Apps/` may depend on anything here.

## Where the look comes from

Nothing here builds `AppResources` any more. The colours the OS chrome draws with and the tray icons
live in the UI's store (`branding/<theme>/manifest.json` in `ui.zip`, written by the web UI's build);
the app reads them through `AppBranding` once the store is in hand, and a head names its theme
(`AppOptions.UiTheme`, `"violet"` for VpnHood Connect). What draws with them - the tray, the window -
waits for `VpnHoodApp.ResourcesLoaded` and appears once, rather than changing under the user. Anything
the manifest does not name falls back to `VpnHood.AppLib.App`'s own defaults.

The page the web host serves is the Avalonia browser build, embedded into *this* assembly from
`src/Apps/AvaloniaUI.Browser`'s publish and handed over as `AppWebRoot.Zip`; the app extracts it.
Nothing is extracted or bound until something asks the app for a web host and calls `EnsureStarted`.

The SPA zip is still embedded - the `VpnHood.AppLib.Assets.ClassicSpa` package, or `use-local-spa.txt` -
but nothing reads it any more.
