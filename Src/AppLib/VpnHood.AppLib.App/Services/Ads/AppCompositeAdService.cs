using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using System.Globalization;
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

    private readonly AsyncLock _loadAdLock = new();
    public async Task LoadAd(IUiContext uiContext, string? countryCode, bool forceReload,
        TimeSpan loadAdTimeout, CancellationToken cancellationToken)
    {
        if (_adProviderItems.Length == 0)
            throw new Exception("There is no AdService registered in this app.");

        using var lockAsync = await _loadAdLock.LockAsync(cancellationToken).Vhc();
        if (!forceReload && !ShouldLoadAd())
            return;

        _loadedAdProviderItem = null;
        var providerExceptions = new List<(string, Exception)>();

        // filter ad services by country code
        var filteredAdProviderItems = _adProviderItems
            .Where(x => countryCode is null || IsCountrySupported(x, countryCode));

        foreach (var adProviderItem in filteredAdProviderItems) {
            cancellationToken.ThrowIfCancellationRequested();

            // find first successful ad network
            try {
                VhLogger.Instance.LogInformation("Trying to load ad. ItemName: {ItemName}", adProviderItem.Name);
                using var timeoutCts = new CancellationTokenSource(loadAdTimeout);
                using var linkedCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
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
                _ = _tracker?.TryTrack(AppTrackerBuilder.BuildShowAdStatus(adProviderItem.Name));
                providerExceptions.Add((adProviderItem.Name, ex));
                VhLogger.Instance.LogWarning(ex, "Could not load any ad. ProviderName: {ProviderName}.", adProviderItem.Name);
            }
        }

        var providerMessages = string.Join(", ", providerExceptions.Select(x => $"{x.Item1}:{x.Item2}"));
        throw new LoadAdException(
            $"Could not load any Ad. " +
            $"CountryCode: {GetCountryName(countryCode)}. Cancelled: {cancellationToken.IsCancellationRequested}. " +
            $"Message: {providerMessages}");
    }

    private static async Task VerifyActiveUi(bool immediately = true)
    {
        if (AppUiContext.Context?.IsActive == true)
            return;

        // throw exception if the UI is not available
        if (immediately)
            throw new ShowAdNoUiException();

        // wait for the UI to be active
        for (var i = 0; i < 3; i++) {
            await Task.Delay(250).Vhc();
            if (AppUiContext.Context?.IsActive == true)
                return;
        }

        throw new ShowAdNoUiException();
    }

    public async Task<string> ShowLoadedAd(IUiContext uiContext, string? customData,
        CancellationToken cancellationToken)
    {
        await VerifyActiveUi();

        if (_loadedAdProviderItem == null)
            throw new LoadAdException("There is no loaded ad.");

        // show the ad
        try {
            VhLogger.Instance.LogInformation("Trying to show ad. ItemName: {ItemName}", _loadedAdProviderItem.Name);
            await _loadedAdProviderItem.AdProvider.ShowAd(uiContext, customData, cancellationToken).Vhc();
            VhLogger.Instance.LogDebug("Showing ad has been completed. {ItemName}", _loadedAdProviderItem.Name);
            await VerifyActiveUi(false); // some ad provider may not raise exception on minimize
            return _loadedAdProviderItem.Name;
        }
        catch (UiContextNotAvailableException) {
            throw new ShowAdNoUiException();
        }
        catch (ShowAdNoUiException) {
            throw;
        }
        catch (Exception ex) {
            await VerifyActiveUi();

            // let's treat unknown error same as LoadException in this version
            throw new LoadAdException("Could not show any ad.", ex);
        }
        finally {
            _loadedAdProviderItem = null;
        }
    }

    private static string GetCountryName(string? countryCode)
    {
        if (string.IsNullOrEmpty(countryCode)) return "n/a";
        try {
            return new RegionInfo(countryCode).Name;
        }
        catch {
            return countryCode;
        }
    }
}