using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.AppLogic.Api.Exceptions;
using VpnHood.AppLogic.Abstractions.Ads;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Monitoring;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.AppLogic.Abstractions.Ads.AdExceptions;

namespace VpnHood.AppLogic.App.Services.Ads;

internal class AppCompositeAdService
{
    // Without a limit of the app's, the network's own ends a stuck load: AdMob gives each of its network
    // calls 60 seconds, which paces the loading bar.
    private static readonly TimeSpan NetworkLoadAdTimeout = TimeSpan.FromSeconds(60);

    private AppAdProviderItem? _loadedAdProviderItem;
    private readonly AppAdProviderItem[] _adProviderItems;
    private readonly TimeSpan _loadAdTimeout;
    private readonly ITracker? _tracker;
    private ProgressMonitor? _progressMonitor;
    public bool IsPreload { get; private set; }

    public readonly record struct ShowLoadedAdResult(string NetworkName, ShowAdResult ShowAdResult);

    public ProgressStatus? LoadAdProgress => _progressMonitor?.Progress;

    // loadAdTimeout: each provider's, or Timeout.InfiniteTimeSpan for none of the app's
    public AppCompositeAdService(AppAdProviderItem[] adProviderItems, TimeSpan loadAdTimeout, ITracker? tracker)
    {
        _adProviderItems = adProviderItems;
        _loadAdTimeout = loadAdTimeout;
        _tracker = tracker;

        // throw exception if an add has both include and exclude country codes
        var appAdProviderItems = _adProviderItems
            .Where(x => x.IncludeCountryCodes.Length > 0 && x.ExcludeCountryCodes.Length > 0)
            .ToArray();

        if (appAdProviderItems.Any())
            throw new Exception(
                $"An ad provider cannot have both include and exclude country codes. ProviderName: {appAdProviderItems.First().Name}");
    }

    private bool ShouldLoadAd()
    {
        return _loadedAdProviderItem?.AdProvider.AdLoadedTime == null ||
               _loadedAdProviderItem.AdProvider.AdLoadedTime + _loadedAdProviderItem.AdProvider.AdLifeSpan <
               DateTime.UtcNow;
    }

    private static bool IsCountrySupported(AppAdProviderItem adProviderItem, string countryCode)
    {
        if (!VhUtils.IsNullOrEmpty(adProviderItem.IncludeCountryCodes))
            return adProviderItem.IncludeCountryCodes.Contains(countryCode, StringComparer.OrdinalIgnoreCase);

        if (!VhUtils.IsNullOrEmpty(adProviderItem.ExcludeCountryCodes))
            return !adProviderItem.ExcludeCountryCodes.Contains(countryCode, StringComparer.OrdinalIgnoreCase);

        return true;
    }

    // A route's networks: through the tunnel only those that can show over the VPN, the internal ad too, and the
    // internal ad only on the last route, as it always shows and would leave a paid ad after it no turn
    private static bool CanFillRoute(AppAdProviderItem adProviderItem, AdRoute route, bool isLastRoute)
    {
        return (route != AdRoute.OverVpn || adProviderItem.CanShowOverVpn) &&
               (!adProviderItem.AdProvider.IsInternal || isLastRoute);
    }

    // the networks that may fill a route where the person is, in the head's order but the internal ad last
    private IEnumerable<AppAdProviderItem> GetRouteItems(AdRoute route, bool isLastRoute, string? countryCode)
    {
        return _adProviderItems
            .Where(x => x.IsEnabled)
            .Where(x => countryCode is null || IsCountrySupported(x, countryCode))
            .Where(x => CanFillRoute(x, route, isLastRoute))
            .OrderBy(x => x.AdProvider.IsInternal);
    }

    // whether a network may fill the route where the person is: on a route that is not the last, a paid one
    public bool CanFill(AdRoute route, bool isLastRoute, string? countryCode)
    {
        return GetRouteItems(route, isLastRoute, countryCode).Any();
    }

    private readonly AsyncLock _loadAdLock = new();

    // the next ad, ahead of its try
    public Task PreloadAd(IUiContext uiContext, string? countryCode, AdRoute route, bool isLastRoute,
        CancellationToken cancellationToken)
    {
        return LoadAd(uiContext, isPreload: true, countryCode, route, isLastRoute, failures: [], cancellationToken);
    }

    // Loads the route's first network that loads, but those that failed in the try (failures, which its own failures
    // join), whose errors the exception lists where none loads
    private async Task LoadAd(IUiContext uiContext, bool isPreload, string? countryCode, AdRoute route,
        bool isLastRoute, Dictionary<AppAdProviderItem, Exception> failures, CancellationToken cancellationToken)
    {
        if (_adProviderItems.Length == 0)
            throw new Exception("There is no AdService registered in this app.");

        var routeItems = GetRouteItems(route, isLastRoute, countryCode)
            .Where(x => !failures.ContainsKey(x))
            .ToArray();

        // an ad loaded before, by a preload say, serves only a route its network may fill
        using var lockAsync = await _loadAdLock.LockAsync(cancellationToken).Vhc();
        if (!ShouldLoadAd() && _loadedAdProviderItem != null && routeItems.Contains(_loadedAdProviderItem))
            return;

        IsPreload = isPreload;
        _loadedAdProviderItem = null;

        _progressMonitor = new ProgressMonitor(
            totalTaskCount: routeItems.Count(x => !x.AdProvider.IsInternal),
            taskTimeout: _loadAdTimeout == Timeout.InfiniteTimeSpan ? NetworkLoadAdTimeout : _loadAdTimeout);

        try {
            foreach (var adProviderItem in routeItems) {
                cancellationToken.ThrowIfCancellationRequested();

                // find first successful ad network
                try {
                    VhLogger.Instance.LogInformation("Trying to load ad. ItemName: {ItemName}", adProviderItem.Name);
                    using var timeoutCts = new CancellationTokenSource(_loadAdTimeout);
                    using var linkedCts =
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                    if (adProviderItem.AdProvider.IsInternal) _progressMonitor = null; // do not track the internal ad
                    await adProviderItem.AdProvider.LoadAd(uiContext, linkedCts.Token).Vhc();
                    _loadedAdProviderItem = adProviderItem;
                    return;
                }
                catch (UiContextNotAvailableException) {
                    throw new ShowAdNoUiException();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    throw; // do not report cancellation
                }
                catch (Exception ex) {
                    _progressMonitor?.IncrementCompleted();
                    var message = string.IsNullOrWhiteSpace(ex.Message)
                        ? $"Empty message. Provider: {adProviderItem.Name}, IsPreload: {isPreload}"
                        : ex.Message;

                    // track the error
                    if (_tracker != null)
                        await _tracker.TryTrack(AppTrackerBuilder.BuildLoadAdFailed(
                                adNetwork: adProviderItem.Name, errorMessage: message, countryCode: countryCode,
                                isPreload: isPreload),
                            cancellationToken).Vhc();

                    failures[adProviderItem] = ex;
                    VhLogger.Instance.LogWarning(ex, "Could not load any ad. ProviderName: {ProviderName}.",
                        adProviderItem.Name);
                }
            }
        }
        finally {
            _progressMonitor = null;
        }

        var providerMessages = failures.Count > 0
            ? string.Join(", ", failures.Select(x => $"{x.Key.Name}:{x.Value.Message}"))
            : "There is no provider for this route and country";

        throw new LoadAdException(
            $"Could not load any Ad. " +
            $"Message: {providerMessages}. " +
            $"Cancelled: {cancellationToken.IsCancellationRequested}.");
    }

    // Loads and shows the route's first network that does both, the internal ad last: a network whose ad loaded but did
    // not show gives its turn to the next. loadAdPostDelay: the wait between a load and its show
    public async Task<ShowLoadedAdResult> LoadAndShowAd(IUiContext uiContext, string customData, string? countryCode,
        AdRoute route, bool isLastRoute, TimeSpan loadAdPostDelay, CancellationToken cancellationToken)
    {
        var failures = new Dictionary<AppAdProviderItem, Exception>();
        while (true) {
            await LoadAd(uiContext, isPreload: false, countryCode, route, isLastRoute, failures, cancellationToken)
                .Vhc();

            // apply delay to prevent showing ads immediately after loading
            await Task.Delay(loadAdPostDelay, cancellationToken).Vhc();

            var result = await TryShowLoadedAd(uiContext, customData, countryCode, failures, cancellationToken).Vhc();
            if (result != null)
                return result.Value;
        }
    }

    // The loaded ad shown, or null where its network failed to show it, which joins the try's failures; no window, or
    // the caller's cancel, ends the try
    private async Task<ShowLoadedAdResult?> TryShowLoadedAd(IUiContext uiContext, string customData,
        string? countryCode, Dictionary<AppAdProviderItem, Exception> failures,
        CancellationToken cancellationToken)
    {
        var adProviderItem = _loadedAdProviderItem ?? throw new LoadAdException("There is no loaded ad.");
        try {
            return await ShowLoadedAd(uiContext, customData, cancellationToken).Vhc();
        }
        catch (ShowAdException ex) when (!cancellationToken.IsCancellationRequested) {
            if (_tracker != null)
                await _tracker.TryTrack(AppTrackerBuilder.BuildShowAdFailed(adNetwork: adProviderItem.Name,
                        errorMessage: ex.Message, countryCode: countryCode, isPreload: IsPreload),
                    cancellationToken).Vhc();

            if (ex is ShowAdNoUiException)
                throw;

            VhLogger.Instance.LogWarning(ex,
                "The ad did not show, so the next network is tried. ProviderName: {ProviderName}.", adProviderItem.Name);

            failures[adProviderItem] = ex;
            return null;
        }
    }

    private async Task<ShowLoadedAdResult> ShowLoadedAd(IUiContext uiContext, string? customData,
        CancellationToken cancellationToken)
    {
        if (_loadedAdProviderItem == null)
            throw new LoadAdException("There is no loaded ad.");

        // show the ad
        try {
            VhLogger.Instance.LogInformation("Trying to show ad. ItemName: {ItemName}", _loadedAdProviderItem.Name);
            var showAdResult = await _loadedAdProviderItem.AdProvider.ShowAd(uiContext, customData, cancellationToken)
                .Vhc();
            VhLogger.Instance.LogDebug("Showing ad has been completed. {ItemName}", _loadedAdProviderItem.Name);
            return new ShowLoadedAdResult(_loadedAdProviderItem.Name, showAdResult);
        }
        catch (UiContextNotAvailableException) {
            throw new ShowAdNoUiException {
                AdNetworkName = _loadedAdProviderItem.Name
            };
        }
        catch (ShowAdNoUiException ex) {
            ex.AdNetworkName = _loadedAdProviderItem.Name;
            throw;
        }
        catch (Exception ex) {
            throw new ShowAdException("Could not show any ad.", ex) { AdNetworkName = _loadedAdProviderItem.Name };
        }
        finally {
            _loadedAdProviderItem = null;
        }
    }
}