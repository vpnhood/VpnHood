using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Devices.Ios.Utils;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Ios.Common;

// App Store counterpart of GooglePlayAppUpdaterProvider. iOS has no in-app update API, so
// IsUpdateAvailable asks the iTunes Lookup API for the released store version (keyed by the bundle
// id, so no per-app configuration) and Update opens the app's App Store page, where the user
// completes — or declines — the update. AppUpdaterService drives the cadence: PromptDelay first
// gives the OS auto-update a chance, and PostponePeriod suppresses re-prompting after Update ran.
// The lookup returns no result for TestFlight-only or unreleased apps, so the provider is
// fail-soft: it reports "no update" and lets the UpdateInfoUrl fallback (if any) take over.
public class AppStoreAppUpdaterProvider : IAppUpdaterProvider
{
    // the lookup result of the last successful check; Update reuses it for the store page URL
    private AppStoreAppInfo? _storeApp;

    public async Task<bool> IsUpdateAvailable(IUiContext uiContext, CancellationToken cancellationToken)
    {
        try {
            VhLogger.Instance.LogDebug("Checking for App Store update availability...");
            var storeApp = await FetchStoreAppInfo(cancellationToken).Vhc();
            if (storeApp == null) {
                VhLogger.Instance.LogDebug("App Store update is not available. App not found on the store.");
                return false;
            }

            _storeApp = storeApp;

            // CFBundleShortVersionString is the store-facing version, so compare against it rather
            // than the assembly version
            var currentVersionString = NSBundle.MainBundle
                .ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString();

            if (!Version.TryParse(storeApp.Version, out var storeVersion) ||
                !Version.TryParse(currentVersionString, out var currentVersion)) {
                VhLogger.Instance.LogDebug(
                    "App Store update is not available. Could not parse versions. Store: {Store}, Current: {Current}",
                    storeApp.Version, currentVersionString);
                return false;
            }

            VhLogger.Instance.LogDebug("App Store version: {StoreVersion}, Current version: {CurrentVersion}",
                storeVersion, currentVersion);
            return storeVersion > currentVersion;
        }
        catch (Exception ex) {
            // return false to allow the alternative way
            VhLogger.Instance.LogWarning(ex, "Could not check for update using the App Store.");
            return false;
        }
    }

    public async Task<bool> Update(IUiContext uiContext, CancellationToken cancellationToken)
    {
        try {
            // reuse the last lookup; fetch again if Update is called without a prior successful check
            var storeApp = _storeApp ?? await FetchStoreAppInfo(cancellationToken).Vhc();
            if (storeApp?.TrackViewUrl == null)
                return false;

            // open the App Store page; the user updates (or declines) there
            VhLogger.Instance.LogDebug("Opening the App Store page for update...");
            var taskCompletionSource = new TaskCompletionSource<bool>();
            await IosUtils.RunOnUiThread(() => {
                var url = new NSUrl(storeApp.TrackViewUrl);
                UIApplication.SharedApplication.OpenUrl(url, new NSDictionary(),
                    success => taskCompletionSource.TrySetResult(success));
            }).Vhc();

            return await taskCompletionSource.Task.WaitAsync(cancellationToken).Vhc();
        }
        catch (Exception ex) {
            // return false to allow the alternative way
            VhLogger.Instance.LogWarning(ex, "Could not update the app using the App Store.");
            return false;
        }
    }

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

    private class AppStoreLookupResult
    {
        [JsonPropertyName("results")]
        public AppStoreAppInfo[] Results { get; init; } = [];
    }

    private class AppStoreAppInfo
    {
        [JsonPropertyName("version")]
        public string? Version { get; init; }

        [JsonPropertyName("trackViewUrl")]
        public string? TrackViewUrl { get; init; }
    }
}
