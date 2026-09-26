# VpnHood.App.Connect

What every distribution of VpnHood Connect shares - Android (Play and web), iOS, Windows and Linux.
It is **not** a library, it is not published, and a forker does not use it: VpnHood Client has its
own (`../../Client/VpnHood.App.Client`), and a fork writes its own of this shape.

## What it is for

Every head under `src/Apps/Connect/` is a thin platform shell: an activity, a view controller, a WPF
window. What is the same for all of them is decided here, once:

| Part | What it decides |
|---|---|
| `ConnectAppOptions.cs` | the options every head shares - the logo, consent summary and look (`UiTheme`, `"violet"`), the settings below, the web host's port, the premium tier, the three packaged files at the paths the asset packages place them - and `IsAddAccessKeySupported`, a constant the desktop commands read too. A head's options factory is this call and its channel's lines on top; `CreatePremium` is how a channel that permits a code box or an outside shop says so |
| `ConnectAppConfigs.cs` | the product's settings: what its private appsettings can say (`AppConfigs` in `VpnHood.AppLib.App`) and Connect's own keys - the portal and its sign-in, the ad networks' ids, the install attribution key. `Load` fills it and takes the built-in key from the head's own `access_key_default.txt` |
| `ConnectAppResources.cs` | the premium feature list (`AppPremiumOptions.Features`) the tier sells |
| the project file | the IP-location database the product ships (`VpnHood.Net.IpLocations.Assets.Ip2LocationLite`), the private appsettings from `.user/VpnHoodConnect`, embedded here once for every head, and the app framework and web host every head builds on |

A setting has no value in code: one the appsettings do not name stays null, and what needs it is
off or fails where it is used - without a portal a head builds no account provider, without an ad
unit id no ad network. The app's ids and names are not settings either: the product states its id
base and name once, in `../Directory.Build.props`, and the build writes them into
each head and into this project (`AppConstants` - see `docs/source-layout.md`): the ids are each head's
own, and the options read the rest here, so a head passes none. A head states only what is
its own - its update feed and its channel's lines - and embeds its own built-in key, since only the
ad-supported build takes the ad key.

## Why a forker does not reference it

This is where *our* answers live. The shape that makes a fork possible is one layer down -
`AppOptions`, the provider interfaces in `VpnHood.AppLib.Abstractions` - all of which ship on NuGet and
know nothing about this project. A fork copies one product folder (`Client/` or `Connect/`) as its
template: its own of this project, and its heads. Nothing beneath `src/Apps/` may depend on anything here.

## Where the look and the page come from

The look lives in the UI's store (`VpnHood.AppUi.Assets.Classic`, `ui.zip`), placed beside each head by
that package's targets. The page the web host serves to a paired device is the Classic Avalonia UI's
browser build (`src/AppUi/VpnHood.AppUi.Presentation.Classic.Avalonia.Browser`), placed the same way as
`assets/web-root.zip`. Neither passes through this project.
