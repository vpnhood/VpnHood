# VpnHood.App.Client

The configuration our own products share — VpnHood Client and VpnHood Connect, across Android, iOS,
Windows and Linux. It is **not** a library, it is not published, and a forker does not use it.

## What it is for

Every head in `src/Apps/` is a thin platform shell: an activity, a view controller, a WPF window. The
decisions that are the same for all of them live here, so they are made once and made identically:

| File | What it decides |
|---|---|
| `IRequiredAppConfigs.cs` | the settings each product must state — `AppId`, `WebUiPort`, `UpdateInfoUrl`, `DefaultAccessKey`, `Ga4MeasurementId`, `RemoteSettingsUrl`, `PrivacyPolicyUrl`, `TermsOfUseUrl` … Each head implements it, so a new product cannot forget one. |
| `ClientAppResources.cs` | VpnHood Client's `AppResources` — the SPA bundle and the branding read from it |
| `ConnectAppResources.cs` | the same for VpnHood Connect: the same SPA bundle, the `connect` branding theme |
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

## How the resources are built

`ClientAppResources` is the worked example of the rule that the head supplies resources and the
libraries never reach for them:

```csharp
public static AppResources Resources => field ??= SpaResourcesFactory.FromSpaZip(SpaZip);
public static IAsset CreateWebRootZip(); // the Avalonia browser build if this build embeds one, else the SPA
```

The same zip serves twice: its branding manifest becomes `AppResources` (colours, tray icons), and its
files are the web root, handed to the app as `AppOptions.WebRootZipAsset` for it to extract. Nothing is
built, extracted or bound until something asks the app for `WebHost` and calls `EnsureStarted`.

The SPA zip is embedded into *this* assembly — in production by the `VpnHood.AppLib.Assets.ClassicSpa`
package's build targets, locally by the `use-local-spa.txt` switch — and its branding manifest carries
the window and tray colours and the tray icons, so rebranding the SPA rebrands the native chrome with
no .NET change. `ConnectAppResources` reads the same zip with the `connect` theme.

Anything the head does not assign falls back to `VpnHood.AppLib.App`'s own defaults, so a build that
embeds no SPA and no branding still runs.
