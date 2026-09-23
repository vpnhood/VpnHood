using VpnHood.AppLib.Api.App;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.AppLib.Api.Exceptions;
using VpnHood.AppLib.Api.Ads;
using VpnHood.AppLib.Api.Countries;
using VpnHood.AppLib.Api.Sessions;
using VpnHood.AppLib.Api.Settings;
using VpnHood.AppLib.Api.SplitTunneling;
using VpnHood.AppLib.Api.Device;

namespace VpnHood.AppLib.Api.HttpClients;

// IAppApi over HTTP: the routes of AppController, one for one.
internal sealed class AppClient(HttpClient httpClient) : AppApiClientBase(httpClient), IAppApi
{
    private const string BaseUrl = "api/app/";

    public Task ProcessTypes(ExceptionType exceptionType, SessionErrorCode errorCode, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This method exists just to let swagger generate types.");
    }

    public Task<AppInfo> Configure(ConfigParams configParams, CancellationToken cancellationToken)
    {
        return HttpPatchAsync<AppInfo>(BaseUrl + "configure", null, configParams, cancellationToken);
    }

    public Task<AppInfo> GetInfo(CancellationToken cancellationToken)
    {
        return HttpGetAsync<AppInfo>(BaseUrl + "info", null, cancellationToken);
    }

    public Task<SplitIpsViaApp> GetSplitIpsViaApp(CancellationToken cancellationToken)
    {
        return HttpGetAsync<SplitIpsViaApp>(BaseUrl + "split-by-ips-via-app", null, cancellationToken);
    }

    public Task SetSplitIpsViaApp(SplitIpsViaApp value, CancellationToken cancellationToken)
    {
        return HttpPutAsync(BaseUrl + "split-by-ips-via-app", null, value, cancellationToken);
    }

    public Task<SplitIpsViaDevice> GetSplitIpsViaDevice(CancellationToken cancellationToken)
    {
        return HttpGetAsync<SplitIpsViaDevice>(BaseUrl + "split-by-ips-via-device", null, cancellationToken);
    }

    public Task SetSplitIpsViaDevice(SplitIpsViaDevice value, CancellationToken cancellationToken)
    {
        return HttpPutAsync(BaseUrl + "split-by-ips-via-device", null, value, cancellationToken);
    }

    public Task<SplitDomains> GetSplitDomains(CancellationToken cancellationToken)
    {
        return HttpGetAsync<SplitDomains>(BaseUrl + "split-by-domains", null, cancellationToken);
    }

    public Task SetSplitDomains(SplitDomains value, CancellationToken cancellationToken)
    {
        return HttpPutAsync(BaseUrl + "split-by-domains", null, value, cancellationToken);
    }

    public Task<AppState> GetState(CancellationToken cancellationToken)
    {
        return HttpGetAsync<AppState>(BaseUrl + "state", null, cancellationToken);
    }

    public Task Connect(Guid? clientProfileId, string? serverLocation, ConnectPlanId planId, CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "connect", ConnectQuery(clientProfileId, serverLocation, planId), null, cancellationToken);
    }

    public Task Diagnose(Guid? clientProfileId, string? serverLocation, ConnectPlanId planId, CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "diagnose", ConnectQuery(clientProfileId, serverLocation, planId), null, cancellationToken);
    }

    private static Dictionary<string, object?> ConnectQuery(Guid? clientProfileId, string? serverLocation, ConnectPlanId planId)
    {
        return new Dictionary<string, object?> {
            ["clientProfileId"] = clientProfileId,
            ["serverLocation"] = serverLocation,
            ["planId"] = planId
        };
    }

    public Task Disconnect(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "disconnect", null, null, cancellationToken);
    }

    public Task ClearLastError(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "clear-last-error", null, null, cancellationToken);
    }

    public Task ClearReconnectRequired(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "clear-reconnect-required", null, null, cancellationToken);
    }

    public Task SetUserSettings(UserSettings userSettings, CancellationToken cancellationToken)
    {
        return HttpPutAsync(BaseUrl + "user-settings", null, userSettings, cancellationToken);
    }

    public Task<string> Log(CancellationToken cancellationToken)
    {
        return HttpGetAsync<string>(BaseUrl + "log.txt", null, cancellationToken);
    }

    public Task<byte[]> PromotionImage(CancellationToken cancellationToken)
    {
        return HttpGetAsync<byte[]>(BaseUrl + "promotion.jpg", null, cancellationToken);
    }

    public Task<IReadOnlyList<DeviceAppInfo>> GetInstalledApps(CancellationToken cancellationToken)
    {
        return HttpGetAsync<IReadOnlyList<DeviceAppInfo>>(BaseUrl + "installed-apps", null, cancellationToken);
    }

    public Task VersionCheck(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "version-check", null, null, cancellationToken);
    }

    public Task VersionCheckPostpone(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "version-check-postpone", null, null, cancellationToken);
    }

    public Task ExtendByRewardedAd(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "extend-by-rewarded-ad", null, null, cancellationToken);
    }

    public Task SetUserReview(AppUserReview userReview, CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "user-review", null, userReview, cancellationToken);
    }

    public async Task<IReadOnlyList<CountryInfo>> GetCountries(CancellationToken cancellationToken)
    {
        return await HttpGetAsync<CountryInfo[]>(BaseUrl + "countries", null, cancellationToken).Vhc();
    }

    public async Task<IReadOnlyList<CountryInfo>> GetSupportedSplitCountries(CancellationToken cancellationToken)
    {
        return await HttpGetAsync<CountryInfo[]>(BaseUrl + "supported-split-by-countries", null, cancellationToken).Vhc();
    }

    public Task InternalAdDismiss(ShowAdResult result, CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "internal-ad/dismiss", new Dictionary<string, object?> { ["result"] = result }, null, cancellationToken);
    }

    public Task InternalAdError(string errorMessage, CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "internal-ad/error", new Dictionary<string, object?> { ["errorMessage"] = errorMessage }, null, cancellationToken);
    }

    public Task<RemoteAccessState> GetRemoteAccess(CancellationToken cancellationToken)
    {
        return HttpGetAsync<RemoteAccessState>(BaseUrl + "remote-access", null, cancellationToken);
    }

    public Task<RemoteAccessState> StartRemoteAccess(CancellationToken cancellationToken)
    {
        return HttpPostAsync<RemoteAccessState>(BaseUrl + "remote-access/start", null, null, cancellationToken);
    }

    public Task StopRemoteAccess(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "remote-access/stop", null, null, cancellationToken);
    }
}
