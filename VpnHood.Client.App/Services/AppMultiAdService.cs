using System.Globalization;
using Microsoft.Extensions.Logging;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Exceptions;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.Services;

internal class AppMultiAdService
{
    public bool IsPreloadApEnabled => _adOptions.PreloadAd;
    private App.AppAdService? _loadedAdService;

    private readonly App.AppAdService[] _adServices;
    private readonly AppAdOptions _adOptions;

    public AppMultiAdService(App.AppAdService[] adServices, AppAdOptions adOptions)
    {
        _adServices = adServices;
        _adOptions = adOptions;

        // throw exception if an add has both include and exclude country codes
        var invalidAdServices = _adServices.Where(x => x.IncludeCountryCodes.Length > 0 && x.ExcludeCountryCodes.Length > 0).ToArray();
        if (invalidAdServices.Any())
            throw new Exception($"An ad service cannot have both include and exclude country codes. ServiceName: {invalidAdServices.First()}");
    }


    private bool ShouldLoadAd()
    {
        return _loadedAdService?.AdProvider.AdLoadedTime == null ||
               (_loadedAdService.AdProvider.AdLoadedTime + _loadedAdService.AdProvider.AdLifeSpan) < DateTime.UtcNow;
    }

    private static bool IsCountrySupported(App.AppAdService adService, string countryCode)
    {
        if (!VhUtil.IsNullOrEmpty(adService.IncludeCountryCodes))
            return adService.IncludeCountryCodes.Contains(countryCode, StringComparer.OrdinalIgnoreCase);

        if (!VhUtil.IsNullOrEmpty(adService.ExcludeCountryCodes))
            return !adService.ExcludeCountryCodes.Contains(countryCode, StringComparer.OrdinalIgnoreCase);

        return true;
    }

    private readonly AsyncLock _loadAdLock = new();
    protected async Task LoadAd(IUiContext uiContext, string? countryCode, bool forceReload, CancellationToken cancellationToken)
    {
        if (_adServices.Length == 0)
            throw new Exception("There is no AdService registered in this app.");

        using var lockAsync = await _loadAdLock.LockAsync(cancellationToken);
        if (!forceReload && !ShouldLoadAd())
            return;

        _loadedAdService = null;

        // filter ad services by country code
        var filteredAdServices = _adServices
            .Where(x => countryCode is null || IsCountrySupported(x, countryCode));

        foreach (var adService in filteredAdServices) {
            cancellationToken.ThrowIfCancellationRequested();

            // find first successful ad network
            try {
                using var timeoutCts = new CancellationTokenSource(_adOptions.LoadAdTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                await adService.AdProvider.LoadAd(uiContext, linkedCts.Token).VhConfigureAwait();
                await Task.Delay(_adOptions.LoadAdPostDelay, cancellationToken);
                _loadedAdService = adService;
                return;
            }
            catch (Exception ex) when (ex is UiContextNotAvailableException || ActiveUiContext.Context != uiContext) {
                throw new ShowAdNoUiException();
            }

            // do not catch if parent cancel the operation
            catch (Exception ex) {
                VhLogger.Instance.LogWarning(ex, "Could not load any ad. ServiceName: {ServiceName}.", adService.Name);
            }
        }

        throw new LoadAdException($"Could not load any AD. CountryCode: {GetCountryName(countryCode)}");
    }

    private static async Task VerifyActiveUi(bool immediately = true)
    {
        if (ActiveUiContext.Context?.IsActive == true)
            return;

        // throw exception if the UI is not available
        if (immediately)
            throw new ShowAdNoUiException();

        // wait for the UI to be active
        for (var i = 0; i < 4; i++) {
            await Task.Delay(500);
            if (ActiveUiContext.Context?.IsActive == true)
                return;
        }

        throw new ShowAdNoUiException();
    }

    protected async Task<string> ShowLoadedAd(IUiContext uiContext, string? customData, CancellationToken cancellationToken)
    {
        await VerifyActiveUi();

        if (_loadedAdService == null)
            throw new LoadAdException("There is no loaded ad.");

        // show the ad
        try {
            await _loadedAdService.AdProvider.ShowAd(uiContext, customData, cancellationToken).VhConfigureAwait();
            await Task.Delay(_adOptions.ShowAdPostDelay, cancellationToken); //wait for finishing trackers
            await VerifyActiveUi(false); // some ad provider may not raise exception on minimize

            return _loadedAdService.Name;
        }
        catch (Exception ex) {
            await VerifyActiveUi();

            // let's treat unknown error same as LoadException in this version
            throw new LoadAdException("Could not show any ad.", ex);
        }
        finally {
            _loadedAdService = null;
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