# VpnHood.App.Client

What every distribution of VpnHood Client shares - Android (Play and web), iOS, Windows and Linux. It
is **not** a library, it is not published, and a forker does not use it: VpnHood Connect has its own
(`../../Connect/Connect`), and a fork writes its own of this shape.

## What it is for

Every head under `src/Apps/Client/` is a thin platform shell: an activity, a view controller, a WPF
window. What is the same for all of them is decided here, once:

| Part | What it decides |
|---|---|
| the project file | the IP-location database the product ships (`VpnHood.Core.IpLocations.Assets.Ip2LocationLite`), and the app framework and web host every head builds on |

The settings each head must state - `AppId`, `UpdateInfoUrl`, `Ga4MeasurementId`, the legal URLs, the
logo and consent names - are `IRequiredAppConfigs` in `VpnHood.AppLib.App`, beside `AppConfigsBase`,
which every head's `AppConfigs` derives from. That contract ships on NuGet, so a fork's head cannot
forget one either.

## Why a forker does not reference it

This is where *our* answers live. The shape that makes a fork possible is one layer down -
`AppOptions`, the provider interfaces in `VpnHood.AppLib.Abstractions` - all of which ship on NuGet and
know nothing about this project. A fork copies one product folder (`Client/` or `Connect/`) as its
template: its own of this project, and its heads. Nothing beneath `src/Apps/` may depend on anything here.

## Where the look and the page come from

The look lives in the UI's store (`VpnHood.AppUi.Assets.Classic`, `ui.zip`), placed beside each head by
that package's targets; the head names its theme (`AppOptions.UiTheme`). The page the web host serves
to a paired device is the Classic Avalonia UI's browser build
(`src/AppUi/Presentation/Classic.Avalonia.Browser`), placed the same way as `assets/web-root.zip`.
Neither passes through this project.