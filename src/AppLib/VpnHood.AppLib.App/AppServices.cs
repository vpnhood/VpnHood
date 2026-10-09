using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Abstractions.Device;
using VpnHood.AppLib.App.Services;
using VpnHood.AppLib.App.Services.Accounts;
using VpnHood.AppLib.App.Services.Proxies;
using VpnHood.AppLib.App.Services.Trackers;
using VpnHood.AppLib.App.Services.Updaters;

namespace VpnHood.AppLib.App;

public class AppServices : IDisposable
{
    public required AccountService? AccountService { get; init; }
    public required AppUpdaterService? UpdaterService { get; init; }
    public required AppProxyEndPointService ProxyEndPointService { get; init; }
    public required SplitCountryService SplitCountryService { get; init; }
    public required SplitIpViaAppService SplitIpViaAppService { get; init; }
    public required SplitDomainService SplitDomainService { get; init; }
    public required SplitDbPublisherService SplitDbPublisherService { get; init; }
    public required IDeviceUiProvider DeviceUiProvider { get; init; }
    public required IAppCultureProvider CultureProvider { get; init; }
    public required IAppUserReviewProvider? UserReviewProvider { get; init; }
    public required AppTrackerService TrackerService { get; init; }
    public void Dispose()
    {
        UpdaterService?.Dispose();
    }
}