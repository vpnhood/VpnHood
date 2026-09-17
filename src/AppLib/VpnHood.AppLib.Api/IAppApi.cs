using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Exceptions;
using VpnHood.AppLib.Api.Ads;
using VpnHood.AppLib.Api.Countries;
using VpnHood.AppLib.Api.Sessions;
using VpnHood.AppLib.Api.Settings;
using VpnHood.AppLib.Api.SplitTunneling;
using VpnHood.AppLib.Api.Device;

// ReSharper disable UnusedMemberInSuper.Global

namespace VpnHood.AppLib.Api;

public interface IAppApi
{
    Task<AppInfo> Configure(ConfigParams configParams, CancellationToken cancellationToken);
    Task<AppInfo> GetInfo(CancellationToken cancellationToken);
    Task<SplitIpsViaApp> GetSplitIpsViaApp(CancellationToken cancellationToken);
    Task SetSplitIpsViaApp(SplitIpsViaApp value, CancellationToken cancellationToken);
    Task<SplitIpsViaDevice> GetSplitIpsViaDevice(CancellationToken cancellationToken);
    Task SetSplitIpsViaDevice(SplitIpsViaDevice value, CancellationToken cancellationToken);
    Task<SplitDomains> GetSplitDomains(CancellationToken cancellationToken);
    Task SetSplitDomains(SplitDomains value, CancellationToken cancellationToken);
    Task<AppState> GetState(CancellationToken cancellationToken);
    Task Connect(Guid? clientProfileId, string? serverLocation, ConnectPlanId planId, CancellationToken cancellationToken);
    Task Diagnose(Guid? clientProfileId, string? serverLocation, ConnectPlanId planId, CancellationToken cancellationToken);
    Task Disconnect(CancellationToken cancellationToken);
    Task ClearLastError(CancellationToken cancellationToken);
    Task ClearReconnectRequired(CancellationToken cancellationToken);
    Task SetUserSettings(UserSettings userSettings, CancellationToken cancellationToken);
    Task<string> Log(CancellationToken cancellationToken);
    Task<byte[]> PromotionImage(CancellationToken cancellationToken);
    Task<IReadOnlyList<DeviceAppInfo>> GetInstalledApps(CancellationToken cancellationToken);
    Task VersionCheck(CancellationToken cancellationToken);
    Task VersionCheckPostpone(CancellationToken cancellationToken);
    Task ExtendByRewardedAd(CancellationToken cancellationToken);
    Task SetUserReview(AppUserReview userReview, CancellationToken cancellationToken);
    Task<CountryInfo[]> GetCountries(CancellationToken cancellationToken);
    Task<CountryInfo[]> GetSupportedSplitCountries(CancellationToken cancellationToken);
    Task InternalAdDismiss(ShowAdResult result, CancellationToken cancellationToken);
    Task InternalAdError(string errorMessage, CancellationToken cancellationToken);
    Task<RemoteAccessState> GetRemoteAccess(CancellationToken cancellationToken);
    Task<RemoteAccessState> StartRemoteAccess(CancellationToken cancellationToken);
    Task StopRemoteAccess(CancellationToken cancellationToken);
}