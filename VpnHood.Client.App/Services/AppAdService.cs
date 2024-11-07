using VpnHood.Client.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Common.IpLocations;

namespace VpnHood.Client.App.Services;

public class AppAdService(
    IRegionProvider regionProvider, 
    AppAdProviderItem[] adProviderItems, 
    AppAdOptions adOptions) :
    IAdService
{
    private readonly AppCompositeAdService _compositeAdService = new(adProviderItems, adOptions);
    public bool IsPreloadAdEnabled => adOptions.PreloadAd;

    public async Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var countryCode = await regionProvider.GetCurrentCountryCodeAsync(cancellationToken);
        await _compositeAdService.LoadAd(uiContext, countryCode: countryCode, forceReload: false, cancellationToken);
    }

    public async Task<ShowedAdResult> ShowAd(IUiContext uiContext, string sessionId, CancellationToken cancellationToken)
    {
        var adData = $"sid:{sessionId};ad:{Guid.NewGuid()}";
        var countryCode = await regionProvider.GetCurrentCountryCodeAsync(cancellationToken);
        await _compositeAdService.LoadAd(uiContext, countryCode: countryCode, forceReload: false, cancellationToken);
        var networkName = await _compositeAdService.ShowLoadedAd(uiContext, adData, cancellationToken);
        var showAdResult = new ShowedAdResult {
            AdData = adData,
            NetworkName = networkName
        };
        return showAdResult;
    }
}