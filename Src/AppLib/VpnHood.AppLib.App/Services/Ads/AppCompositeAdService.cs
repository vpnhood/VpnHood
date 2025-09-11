using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Abstractions.AdExceptions;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services.Ads;


internal class AppCompositeAdService
{
    private AppAdProviderItem? _loadedAdProviderItem;
    private readonly AppAdProviderItem[] _adProviderItems;
    private readonly ITracker? _tracker;
    private ProgressTracker? _progressTracker;
    public bool IsPreload { get; private set; }
    public readonly record struct ShowLoadedAdResult(string NetworkName, ShowAdResult ShowAdResult);
    public ProgressStatus? LoadAdProgress => _progressTracker?.Progress;

    public AppCompositeAdService(AppAdProviderItem[] adProviderItems, ITracker? tracker)
    {
        _adProviderItems = adProviderItems;
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
               DateTime.Now;
    }

    private static bool IsCountrySupported(AppAdProviderItem adProviderItem, string countryCode)
    {
        if (!VhUtils.IsNullOrEmpty(adProviderItem.IncludeCountryCodes))
            return adProviderItem.IncludeCountryCodes.Contains(countryCode, StringComparer.OrdinalIgnoreCase);

        if (!VhUtils.IsNullOrEmpty(adProviderItem.ExcludeCountryCodes))
            return !adProviderItem.ExcludeCountryCodes.Contains(countryCode, StringComparer.OrdinalIgnoreCase);

        return true;
    }

    private readonly AsyncLock _loadAdLock = new();

    public async Task LoadAd(IUiContext uiContext,
        bool isPreload, string? countryCode, bool forceReload,
        TimeSpan loadAdTimeout, bool useFallback,
        CancellationToken cancellationToken)
    {
        if (_adProviderItems.Length == 0)
            throw new Exception("There is no AdService registered in this app.");

        using var lockAsync = await _loadAdLock.LockAsync(cancellationToken).Vhc();
        if (!forceReload && !ShouldLoadAd())
            return;

        IsPreload = isPreload;
        _loadedAdProviderItem = null;
        var providerExceptions = new List<(string, Exception)>();

        // filter ad services by country code
        var filteredAdProviderItems = _adProviderItems
            .Where(x => x.IsEnabled)
            .Where(x => countryCode is null || IsCountrySupported(x, countryCode))
            .Where(x => useFallback || !x.IsFallback)
            .ToArray();

        _progressTracker = new ProgressTracker(
            totalTaskCount: filteredAdProviderItems.Count(x => !x.IsFallback),
            taskTimeout: loadAdTimeout);

        foreach (var adProviderItem in filteredAdProviderItems) {
            cancellationToken.ThrowIfCancellationRequested();

            // find first successful ad network
            try {
                VhLogger.Instance.LogInformation("Trying to load ad. ItemName: {ItemName}", adProviderItem.Name);
                using var timeoutCts = new CancellationTokenSource(loadAdTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                if (adProviderItem.IsFallback) _progressTracker = null; // do not track fallback ads
                await adProviderItem.AdProvider.LoadAd(uiContext, linkedCts.Token).Vhc();
                _loadedAdProviderItem = adProviderItem;
                _progressTracker = null;
                return;
            }
            catch (UiContextNotAvailableException) {
                _progressTracker = null;
                throw new ShowAdNoUiException();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                _progressTracker = null;
                throw; // do not report cancellation
            }
            catch (Exception ex) {
                _progressTracker?.IncrementCompleted();
                var message = string.IsNullOrWhiteSpace(ex.Message)
                    ? $"Empty message. Provider: {adProviderItem.Name}, IsPreload: {isPreload}"
                    : ex.Message;

                // track the error
                if (_tracker != null)
                    await _tracker.TryTrackWithCancellation(AppTrackerBuilder.BuildLoadAdFailed(
                            adNetwork: adProviderItem.Name, errorMessage: message, countryCode: countryCode,
                            isPreload: isPreload),
                        cancellationToken);

                providerExceptions.Add((adProviderItem.Name, ex));
                VhLogger.Instance.LogWarning(ex, "Could not load any ad. ProviderName: {ProviderName}.",
                    adProviderItem.Name);
            }
        }

        var providerMessages = filteredAdProviderItems.Any()
            ? string.Join(", ", providerExceptions.Select(x => $"{x.Item1}:{x.Item2.Message}"))
            : "There is no provider for this country";

        throw new LoadAdException(
            $"Could not load any Ad. " +
            $"Message: {providerMessages}. " +
            $"Cancelled: {cancellationToken.IsCancellationRequested}.");
    }

    public async Task<ShowLoadedAdResult> ShowLoadedAd(IUiContext uiContext, string? customData,
        CancellationToken cancellationToken)
    {
        if (_loadedAdProviderItem == null)
            throw new LoadAdException("There is no loaded ad.");

        // show the ad
        try {
            VhLogger.Instance.LogInformation("Trying to show ad. ItemName: {ItemName}", _loadedAdProviderItem.Name);
            var showAdResult = await _loadedAdProviderItem.AdProvider.ShowAd(uiContext, customData, cancellationToken).Vhc();
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