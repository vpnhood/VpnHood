using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Updaters;
using VpnHood.Core.Client.Devices.Ios.Utils;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.Stores.AppStore;

// App Store counterpart of GooglePlayAppUpdaterProvider. iOS has no in-app update API, so Check
// asks the iTunes Lookup API for the released store version (keyed by the bundle id, so no per-app
// configuration) and StartUpdate opens the app's App Store page, where the user completes — or
// declines — the update. AppUpdaterService drives the cadence: PromptDelay first gives the OS
// auto-update a chance, and PostponePeriod suppresses re-prompting after the page opened. The lookup
// returns no result for TestFlight-only or unreleased apps: "no update". A failure throws, which the
// service treats like "none".
public class AppStoreAppUpdaterProvider : IAppUpdaterProvider
{
    // the lookup result of the last successful check; StartUpdate reuses it for the store page URL
    private AppStoreAppInfo? _storeApp;
    private AppUpdateState _state = AppUpdateState.None;

    public bool HasOwnScreen => true;

    public async Task<AppUpdateState> Check(AppRelease? feedRelease, IUiContext? uiContext,
        CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Checking for App Store update availability...");
        var storeApp = await FetchStoreAppInfo(cancellationToken).Vhc();
        if (storeApp == null) {
            VhLogger.Instance.LogDebug("App Store update is not available. App not found on the store.");
            _state = AppUpdateState.None;
            return _state;
        }

        _storeApp = storeApp;

        // Compare against the assembly version — the same source AppUpdaterService and the UI use.
        // Every VpnHood assembly carries the one version from Directory.Build.props, so the
        // abstractions' version is VpnHoodApp's, and this package needs no reference to the app.
        // NOT CFBundleShortVersionString: the .NET iOS SDK injects that from ApplicationDisplayVersion,
        // which only pub/lib/Publish-IosApp.ps1 sets, so every non-CI build reports the SDK default
        // "1.0" and would see the live store listing as an endless update.
        var currentVersion = typeof(IAppUpdaterProvider).Assembly.GetName().Version;
        if (!Version.TryParse(storeApp.Version, out var storeVersion) || currentVersion == null) {
            VhLogger.Instance.LogDebug(
                "App Store update is not available. Could not parse versions. Store: {Store}, Current: {Current}",
                storeApp.Version, currentVersion);
            _state = AppUpdateState.None;
            return _state;
        }

        VhLogger.Instance.LogDebug("App Store version: {StoreVersion}, Current version: {CurrentVersion}",
            storeVersion, currentVersion);

        // Compare Major.Minor.Build only: the store's marketing version has no revision field, so an
        // unnormalized compare comes out on the undefined revision (-1) rather than the real numbers.
        _state = Normalize(storeVersion) > Normalize(currentVersion)
            ? new AppUpdateState { Stage = AppUpdateStage.Available }
            : AppUpdateState.None;
        return _state;
    }

    public AppUpdateState GetState(AppRelease? feedRelease)
    {
        return _state;
    }

    public async Task StartUpdate(AppRelease? feedRelease, IUiContext? uiContext,
        CancellationToken cancellationToken)
    {
        // the store page opens over the window
        if (uiContext is null)
            throw new UiContextNotAvailableException();

        // reuse the last lookup; fetch again if StartUpdate is called without a prior successful check
        var storeApp = _storeApp ?? await FetchStoreAppInfo(cancellationToken).Vhc();
        var trackViewUrl = storeApp?.TrackViewUrl ??
                           throw new InvalidOperationException("The App Store has no page for the app.");

        // open the App Store page; the user updates (or declines) there
        VhLogger.Instance.LogDebug("Opening the App Store page for update...");
        // completed on the main thread by the OpenUrl callback; do not resume inline on it
        var taskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await IosUtils.RunOnUiThread(() => {
            var url = new NSUrl(trackViewUrl);
            UIApplication.SharedApplication.OpenUrl(url, new NSDictionary(),
                success => taskCompletionSource.TrySetResult(success));
        }).Vhc();

        if (!await taskCompletionSource.Task.WaitAsync(cancellationToken).Vhc())
            throw new InvalidOperationException("Could not open the App Store page.");
    }

    // the App Store installs on its own page: nothing waits for the app's Install
    public Task CompleteUpdate(AppRelease? feedRelease, IUiContext? uiContext,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The App Store installs its update on its own page.");
    }

    // the update runs in the App Store app, so the app has none of its own to bring back
    public Task<bool> ResumeUpdate(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static async Task<AppStoreAppInfo?> FetchStoreAppInfo(CancellationToken cancellationToken)
    {
        var bundleId = NSBundle.MainBundle.BundleIdentifier ??
                       throw new InvalidOperationException("Could not get the bundle identifier.");

        // ReSharper disable once ShortLivedHttpClient
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        var lookupJson = await httpClient
            .GetStringAsync($"https://itunes.apple.com/lookup?bundleId={bundleId}", cancellationToken).Vhc();

        var lookupResult = JsonUtils.Deserialize<AppStoreLookupResult>(lookupJson);
        return lookupResult.Results.FirstOrDefault();
    }

    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Local")]
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Local")]
    private class AppStoreLookupResult
    {
        [JsonPropertyName("results")]
        public AppStoreAppInfo[] Results { get; init; } = [];
    }

    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Local")]
    private class AppStoreAppInfo
    {
        [JsonPropertyName("version")]
        public string? Version { get; init; }

        [JsonPropertyName("trackViewUrl")]
        public string? TrackViewUrl { get; init; }
    }
}
