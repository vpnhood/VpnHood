using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.Ads;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Countries;
using VpnHood.AppLib.Api.Device;
using VpnHood.AppLib.Api.Settings;
using VpnHood.AppLib.Api.SplitTunneling;

namespace VpnHood.App.StoreScreenshots;

// The app's own API, answered from the fixture: the configuration and the state are the document,
// the settings are whatever the page last saved, and the installed apps are the fixture's. What a
// screenshot page never calls is not answered (UnmockedCalls); the calls that leave no trace on a
// screen are accepted and dropped.
internal sealed class FakeAppApi(AppInfo info, IReadOnlyList<DeviceAppInfo> installedApps) : IAppApi
{
    public AppInfo Info { get; private set; } = info;

    public Task<AppInfo> Configure(ConfigParams configParams, CancellationToken cancellationToken) => Task.FromResult(Info);
    public Task<AppInfo> GetInfo(CancellationToken cancellationToken) => Task.FromResult(Info);
    public Task<AppState> GetState(CancellationToken cancellationToken) => Task.FromResult(Info.State);
    public Task<IReadOnlyList<DeviceAppInfo>> GetInstalledApps(CancellationToken cancellationToken) => Task.FromResult(installedApps);
    public Task<IReadOnlyList<CountryInfo>> GetCountries(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CountryInfo>>([]);
    public Task<IReadOnlyList<CountryInfo>> GetSupportedSplitCountries(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CountryInfo>>([]);

    // the settings a page saves come back as the app would report them: the same object, unchanged
    public Task SetUserSettings(UserSettings userSettings, CancellationToken cancellationToken)
    {
        Info = new AppInfo {
            Features = Info.Features,
            IntentFeatures = Info.IntentFeatures,
            State = Info.State,
            UserSettings = userSettings,
            ClientProfileInfos = Info.ClientProfileInfos,
            AvailableCultureInfos = Info.AvailableCultureInfos,
            IsRemote = Info.IsRemote
        };
        return Task.CompletedTask;
    }

    // housekeeping a page does on arrival, with nothing to show for it
    public Task ClearLastError(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ClearReconnectRequired(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task VersionCheckPostpone(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<SplitIpsViaApp> GetSplitIpsViaApp(CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.GetSplitIpsViaApp");
    public Task SetSplitIpsViaApp(SplitIpsViaApp value, CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.SetSplitIpsViaApp");
    public Task<SplitIpsViaDevice> GetSplitIpsViaDevice(CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.GetSplitIpsViaDevice");
    public Task SetSplitIpsViaDevice(SplitIpsViaDevice value, CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.SetSplitIpsViaDevice");
    public Task<SplitDomains> GetSplitDomains(CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.GetSplitDomains");
    public Task SetSplitDomains(SplitDomains value, CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.SetSplitDomains");
    public Task Connect(Guid? clientProfileId, string? serverLocation, ConnectPlanId planId, CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.Connect");
    public Task Diagnose(Guid? clientProfileId, string? serverLocation, ConnectPlanId planId, CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.Diagnose");
    public Task Disconnect(CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.Disconnect");
    public Task<string> Log(CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.Log");
    public Task<byte[]> PromotionImage(CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.PromotionImage");
    public Task VersionCheck(CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.VersionCheck");
    public Task ExtendByRewardedAd(CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.ExtendByRewardedAd");
    public Task SetUserReview(AppUserReview userReview, CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.SetUserReview");
    public Task InternalAdDismiss(ShowAdResult result, CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.InternalAdDismiss");
    public Task InternalAdError(string errorMessage, CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.InternalAdError");
    public Task<RemoteAccessState> GetRemoteAccess(CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.GetRemoteAccess");
    public Task<RemoteAccessState> StartRemoteAccess(CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.StartRemoteAccess");
    public Task StopRemoteAccess(CancellationToken cancellationToken) => throw UnmockedCalls.Record("App.StopRemoteAccess");
}
