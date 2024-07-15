using Microsoft.Extensions.Logging;
using System.Globalization;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.Exceptions;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Exceptions;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.Services;

internal class AppInternalAdService(IAppAdService[] adServices, AppAdOptions adOptions)
{
    public IAppAdService[] AdServices => adServices;
    public IAppAdService? LoadedAdService { get; internal set; }
    public bool IsPreloadApEnabled => adOptions.PreloadAd;

    public bool ShouldLoadAd()
    {
        return LoadedAdService?.AdLoadedTime == null ||
               (LoadedAdService.AdLoadedTime + LoadedAdService.AdLifeSpan) < DateTime.UtcNow;
    }

    private readonly AsyncLock _loadAdLock = new();

    public async Task LoadAd(IUiContext uiContext, string? countryCode, bool forceReload,
        CancellationToken cancellationToken)
    {
        using var lockAsync = await _loadAdLock.LockAsync(cancellationToken);
        if (!forceReload && !ShouldLoadAd())
            return;

        LoadedAdService = null;

        // filter ad services by country code
        var filteredAdServices = adServices
            .Where(x => countryCode is null || x.IsCountrySupported(countryCode));

        var noFillAdNetworks = new List<string>();
        foreach (var adService in filteredAdServices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // find first successful ad network
            try
            {
                if (noFillAdNetworks.Contains(adService.NetworkName))
                    continue;

                using var timeoutCts = new CancellationTokenSource(adOptions.LoadAdTimeout);
                using var linkedCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                await adService.LoadAd(uiContext, linkedCts.Token).VhConfigureAwait();
                await Task.Delay(adOptions.LoadAdPostDelay, cancellationToken);
                LoadedAdService = adService;
                return;
            }
            catch (NoFillAdException)
            {
                noFillAdNetworks.Add(adService.NetworkName);
            }
            catch (Exception ex) when (ex is UiContextNotAvailableException || ActiveUiContext.Context != uiContext)
            {
                throw new ShowAdNoUiException();
            }

            // do not catch if parent cancel the operation
            catch (Exception ex)
            {
                VhLogger.Instance.LogWarning(ex, "Could not load any ad. Network: {Network}.", adService.NetworkName);
            }
        }

        throw new LoadAdException($"Could not load any AD. Country: {GetCountryName(countryCode)}");
    }

    public async Task ShowAd(IUiContext uiContext, string? customData, CancellationToken cancellationToken)
    {
        if (LoadedAdService == null)
            throw new LoadAdException("Could not load any ad.");

        // show the ad
        try
        {
            await LoadedAdService.ShowAd(uiContext, customData, cancellationToken).VhConfigureAwait();
            await Task.Delay(adOptions.ShowAdPostDelay, cancellationToken); //wait for finishing trackers
            if (ActiveUiContext.Context != uiContext) // some ad provider may not raise exception on minimize
                throw new ShowAdNoUiException();
        }
        catch (Exception ex)
        {
            if (ActiveUiContext.Context != uiContext)
                throw new ShowAdNoUiException();

            // let's treat unknown error same as LoadException in this version
            throw new LoadAdException("Could not show any ad.", ex);
        }
        finally
        {
            LoadedAdService = null;
        }
    }

    public static string GetCountryName(string? countryCode)
    {
        if (string.IsNullOrEmpty(countryCode)) return "n/a";
        try { return new RegionInfo(countryCode).Name; }
        catch { return countryCode; }
    }
}
