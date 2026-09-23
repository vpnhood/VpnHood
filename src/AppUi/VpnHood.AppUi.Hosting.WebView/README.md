# VpnHood.AppUi.Hosting.WebView

Shared, platform-neutral hosting for the VpnHood SPA (the web UI served by
`VpnHoodAppWebHost` and shown in a native web view). One controller holds **all** the
hosting business logic; each OS supplies only a thin adapter for the native web-view mechanics.

## Why

Every platform used to re-implement the same logic — start the web server, compute the launch
URL, show a spinner, show an error page, reload when the server self-heals, run the recovery
state machine, forward resume. Only the *native web-view calls* actually differed. This project
extracts the common part so a new platform (MAUI, Mac Catalyst, WinRT, …) is just one adapter.

## Pattern: composition (Bridge), not a base class

Each OS host already inherits a platform type it can't give up (`UIViewController`, an Android
activity handler, a WPF `Window`, a MAUI `ContentPage`), and C# has no multiple inheritance. So
the shared logic is a **controller** (`WebViewHost`) that talks to a per-OS **adapter**
(`IWebView`). The host creates the adapter + controller and forwards native lifecycle calls.

```
 OS host (UIViewController / Activity handler / Window / ContentPage)
   ├─ owns → WebViewHost        (SHARED business logic — this project)
   │            └─ drives → IWebView   (the ONLY per-OS code)
   └─ forwards native lifecycle (create / resume / destroy) to the host
```

## Types

- **`IWebView`** — the per-OS surface: `Initialize`, `Load(Uri)`, `SetLoading(bool)`,
  `ShowError(string)`, `Post(Action)` (marshal to the UI thread), and events `PageLoaded`,
  `LoadFailed`, `ContentProcessGone`. Threading contract: the host calls these on the UI thread and
  expects the events on the UI thread. Adapters only report; they never decide.
- **`WebViewHost`** — owns server `Init`, launch-URL computation (`?nocache={SpaHash}` plus an
  optional `LaunchUrlBuilder` hook), resume→`AppUiContext.NotifyResumed()`, and reload after a
  failure or server restart.
- **`WebViewHostOptions`** — `LaunchUrlBuilder`.

## Keeping the server up (in `VpnHoodAppWebHost`)

Three small mechanisms, each tied to a concrete signal. There is deliberately no periodic connect
probe: one that times out on a busy or dozing device restarts a healthy server, and every restart
kills whatever the web view is loading at that moment (blank page, or the old "user interface is
not responding" screen).

1. **Watchdog** — a 5 s timer restarts the listener when `IsListening` is false. CavemanTcp clears
   the flag when its accept loop exits, so this only ever restarts a listener that is actually gone.
   After a successful restart, `Restarted` makes the native host reload the SPA, including assets
   whose loading was interrupted after the main document finished.
2. **Resume probe** — the server subscribes to `AppUiContext.OnResumed` itself. iOS suspends the
   process and can close the loopback socket meanwhile while the accept loop still believes it is
   listening, so one real connect per resume, restart if it fails. The host then reloads as in 1.
3. **Load again after a failure** (in `WebViewHost`) — `LoadFailed` (main-document connection
   failure) or `ContentProcessGone` makes the host give the server one real connect check (a failed
   load is a concrete "unreachable" signal, and the flag can lie after an iOS suspension), then load
   the SPA again after 1 s (Android rebuilds its WebView before reporting). The delay is the only bound: a listener that keeps failing is retried
   every second instead of dead-ending on an error screen, and the watchdog has brought it back by
   then. The fatal error screen is reserved for `Start()` throwing (the server cannot bind at all).

Recovery lives in the native host; `index.html` has no bundle-error reload handler. A successful
server restart reloads the whole document after 1 s, coalescing with any pending failure reload.
This covers assets interrupted by a server outage; it does not detect arbitrary bundle failures
while the server remains healthy. A full reload resets the current route and unsaved UI state.

## Adding / owning a platform

1. Implement `IWebView` wrapping the native web view; raise the events from its navigation
   callbacks (drop cancelled/superseded loads, e.g. iOS `NSURLErrorCancelled -999`).
2. In the OS host: construct the adapter, `new WebViewHost(adapter)`, call `Start()` on create,
   `OnResume()` from the platform's foreground/resume hook, `Dispose()` on teardown.
3. Keep OS-only chrome (safe area, status bar, tray icon, hardware back, window state) in the host.

## Current adapters

| Platform | Adapter | Host | Web view |
|---|---|---|---|
| iOS | `IosWebView` (WebView.Ios) | `IosWebViewController` | `WKWebView` |
| Android | `AndroidWebView` (WebView.Android) | `AndroidWebViewMainActivityHandler` | `Android.Webkit.WebView` |
| Windows (WPF) | `WpfWebView` (WebView.Windows) | `VpnHoodWpfMainWindow` | WebView2 |
| MAUI | `MauiWebView` (WebView.Maui) | `VpnHoodWebViewPage` | `Microsoft.Maui.Controls.WebView` |

## Build / verification status

- **iOS** — built Release and device-verified (launches, server starts, background→foreground
  recovers).
- **Windows (WPF)** — build-verified (adapter + full `Client.Windows.Web` app). Still smoke-test at
  runtime (WebView2 present + runtime-missing fallback).
- **Android** — build-verified (adapter + full `Client.Android.Web` app). Still smoke-test on a
  device (content-view swap, hardware back, background→foreground recovery).
- **MAUI** — build-verified (adapter, both android + windows target frameworks). Greenfield host —
  smoke-test resume delivery and the loading/error visuals on a device.

The three above were compile-checked against the toolchains but not yet runtime-smoke-tested.

### Per-platform things to verify

- **Android** — content-view swap (loader → WebView on first `PageLoaded`); WebView-version
  "update WebView" redirect (`ResolveUrl`); hardware back (API<33 `OnKeyDown` + API33+ back
  callback). The old `KillSpaServer` debug OnPause/OnResume hook was dropped (the watchdog
  supersedes it). `LoadFailed` is raised for main-frame connection errors; a dead render process
  rebuilds the WebView, then raises `ContentProcessGone`.
- **Windows** — the SPA URL now carries `?nocache={SpaHash}` (it didn't before); the WebView2
  "runtime missing" fallback (hide window + open system browser) moved into `OnWebView2Unavailable`;
  the window hides rather than closes, so `_host` is not explicitly disposed (process exit handles
  it).
- **MAUI** — greenfield (no SPA host existed before). Verify the `Dispatcher` is non-null when the
  page is constructed, and that resume is delivered (`Window.Resumed` + `OnAppearing`).
