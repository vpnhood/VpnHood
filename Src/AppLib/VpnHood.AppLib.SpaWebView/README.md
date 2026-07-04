# VpnHood.AppLib.SpaWebView

Shared, platform-neutral hosting for the VpnHood SPA (the web UI served by
`VpnHoodAppWebServer` and shown in a native web view). One controller holds **all** the
hosting business logic; each OS supplies only a thin adapter for the native web-view mechanics.

## Why

Every platform used to re-implement the same logic — start the web server, compute the launch
URL, show a spinner, show an error page, reload when the server self-heals, run the recovery
state machine, forward resume. Only the *native web-view calls* actually differed. This project
extracts the common part so a new platform (MAUI, Mac Catalyst, WinRT, …) is just one adapter.

## Pattern: composition (Bridge), not a base class

Each OS host already inherits a platform type it can't give up (`UIViewController`, an Android
activity handler, a WPF `Window`, a MAUI `ContentPage`), and C# has no multiple inheritance. So
the shared logic is a **controller** (`SpaWebViewHost`) that talks to a per-OS **adapter**
(`ISpaWebView`). The host creates the adapter + controller and forwards native lifecycle calls.

```
 OS host (UIViewController / Activity handler / Window / ContentPage)
   ├─ owns → SpaWebViewHost        (SHARED business logic — this project)
   │            └─ drives → ISpaWebView   (the ONLY per-OS code)
   └─ forwards native lifecycle (create / resume / destroy) to the host
```

## Types

- **`ISpaWebView`** — the per-OS surface: `Initialize`, `Load(Uri)`, `Reload`, `SetLoading(bool)`,
  `ShowError(string)`, `Post(Action)` (marshal to the UI thread), and events `PageLoaded`,
  `LoadFailed`, `ContentProcessGone`. Threading contract: the host calls these on the UI thread and
  expects the events on the UI thread.
- **`SpaWebViewHost`** — owns server `Init`, launch-URL computation (`?nocache={SpaHash}` plus an
  optional `LaunchUrlBuilder` hook), `Restarted`→reload, resume→`AppUiContext.NotifyResumed()`, and
  the bounded recovery state machine.
- **`SpaWebViewHostOptions`** — `LaunchUrlBuilder`, `ServerNotRespondingMessage`,
  `MaxRecoveryAttempts`.
- **`SpaLoadFailedEventArgs`** — `DuringInitialConnect` (true ⇒ the listener is unusable even if it
  still reports up, so force a server restart rather than just reloading).

## Server self-heal (in `VpnHoodAppWebServer`)

The listener can be torn down under the app (iOS backgrounding especially). The server heals
itself via a **1-second health watchdog** plus an `AppUiContext.OnResumed` signal, both gated on a
**real loopback connect probe** (`IsListening` can go stale). It raises `Restarted` when it rebinds
so the UI reloads. The probe retries a few times so it tolerates the accept-loop warmup window
instead of restarting spuriously right after launch.

## Adding / owning a platform

1. Implement `ISpaWebView` wrapping the native web view; raise the events from its navigation
   callbacks (drop cancelled/superseded loads, e.g. iOS `NSURLErrorCancelled -999`).
2. In the OS host: construct the adapter, `new SpaWebViewHost(adapter)`, call `Start()` on create,
   `OnResume()` from the platform's foreground/resume hook, `Dispose()` on teardown.
3. Keep OS-only chrome (safe area, status bar, tray icon, hardware back, window state) in the host.

## Current adapters

| Platform | Adapter | Host | Web view |
|---|---|---|---|
| iOS | `IosSpaWebView` (Ios.Common) | `VpnHoodAppWebViewController` | `WKWebView` |
| Android | `AndroidSpaWebView` (Android.Common) | `AndroidAppWebViewMainActivityHandler` | `Android.Webkit.WebView` |
| Windows (WPF) | `WpfSpaWebView` (Win.Common.WpfSpa) | `VpnHoodWpfSpaMainWindow` | WebView2 |
| MAUI | `MauiSpaWebView` (Maui.Common) | `VpnHoodSpaPage` | `Microsoft.Maui.Controls.WebView` |

## Build / verification status

- **iOS** — built Release and device-verified (launches, server starts, background→foreground
  recovers).
- **Android / Windows / MAUI** — written against the existing native code and the iOS reference,
  but **not build-verified** (no Android SDK / no Windows / no MAUI workloads in the authoring
  environment). Build and smoke-test each on its toolchain.

### Per-platform things to verify

- **Android** — content-view swap (loader → WebView on first `PageLoaded`); WebView-version
  "update WebView" redirect (`ResolveUrl`); hardware back (API<33 `OnKeyDown` + API33+ back
  callback). The old `KillSpaServer` debug OnPause/OnResume hook was dropped (the health monitor
  supersedes it). `LoadFailed`/`ContentProcessGone` are not raised — recovery is via the health
  monitor + resume + `Restarted`.
- **Windows** — the SPA URL now carries `?nocache={SpaHash}` (it didn't before); the WebView2
  "runtime missing" fallback (hide window + open system browser) moved into `OnWebView2Unavailable`;
  the window hides rather than closes, so `_host` is not explicitly disposed (process exit handles
  it).
- **MAUI** — greenfield (no SPA host existed before). Verify the `Dispatcher` is non-null when the
  page is constructed, and that resume is delivered (`Window.Resumed` + `OnAppearing`); the 1s
  health monitor is the real safety net regardless.
