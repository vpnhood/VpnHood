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

/// <summary>
/// Provides the App Store's update: iOS has no in-app update API, so the check asks the iTunes Lookup API for the
/// released store version, and the update opens the app's App Store page, where the person updates or declines.
/// </summary>
/// <remarks>
/// The lookup is keyed by the bundle ID, so the app needs no configuration of its own. It finds nothing for an app only on
/// TestFlight or not released, which is no update; a failed lookup throws.
/// </remarks>
/// <param name="appVersion">The installed app's version, which the store's is compared with.</param>
public class AppStoreAppUpdaterProvider(Version appVersion) : IAppUpdaterProvider
{
    // the lookup result of the last successful check; StartUpdate reuses it for the store page URL
    private AppStoreAppInfo? _storeApp;
    private AppUpdateState _state = AppUpdateState.None;

    /// <inheritdoc/>
    /// <remarks>It is <see langword="true"/>: the App Store's page shows the update.</remarks>
    public bool HasOwnScreen => true;

    /// <inheritdoc/>
    /// <remarks>It is <see langword="false"/>: the App Store installs the update, and stops the app itself.</remarks>
    public bool IsInstalling => false;

    /// <inheritdoc/>
    /// <remarks>
    /// It asks the iTunes Lookup API, and compares the store's version with the installed one by their major, minor and
    /// build numbers.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The app's bundle ID cannot be read.</exception>
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

        if (!Version.TryParse(storeApp.Version, out var storeVersion)) {
            VhLogger.Instance.LogDebug(
                "App Store update is not available. Could not parse the store's version. Store: {Store}",
                storeApp.Version);
            _state = AppUpdateState.None;
            return _state;
        }

        VhLogger.Instance.LogDebug("App Store version: {StoreVersion}, Current version: {CurrentVersion}",
            storeVersion, appVersion);

        // Compare Major.Minor.Build only: the store's marketing version has no revision field, so an
        // unnormalized compare comes out on the undefined revision (-1) rather than the real numbers.
        _state = Normalize(storeVersion) > Normalize(appVersion)
            ? new AppUpdateState { Stage = AppUpdateStage.Available }
            : AppUpdateState.None;
        return _state;
    }

    /// <inheritdoc/>
    /// <remarks>It gives the last check's state.</remarks>
    public AppUpdateState GetState(AppRelease? feedRelease)
    {
        return _state;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It opens the app's App Store page, from the last check's lookup or a new one, and completes once the page is open.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The app's bundle ID cannot be read, the App Store has no page for the app, or iOS did not open it.
    /// </exception>
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

    /// <inheritdoc/>
    /// <remarks>
    /// It always throws <see cref="NotSupportedException"/>: the App Store installs on its own page.
    /// </remarks>
    public Task CompleteUpdate(AppRelease? feedRelease, IUiContext? uiContext,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The App Store installs its update on its own page.");
    }

    /// <inheritdoc/>
    /// <remarks>It gives <see langword="false"/>: the update runs in the App Store app.</remarks>
    public Task<bool> ResumeUpdate(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    /// <remarks>It is <see langword="false"/>: the App Store keeps its own automatic update setting.</remarks>
    public bool IsAutomaticUpdateSupported => false;

    /// <inheritdoc/>
    /// <remarks>It always throws <see cref="NotSupportedException"/>.</remarks>
    public Task<bool> IsAutomaticUpdateOn(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The App Store keeps its own automatic update setting.");
    }

    /// <inheritdoc/>
    /// <remarks>It always throws <see cref="NotSupportedException"/>.</remarks>
    public Task SetAutomaticUpdate(bool isOn, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The App Store keeps its own automatic update setting.");
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
