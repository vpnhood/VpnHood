using VpnHood.AppLib.Abstractions.Ads;
using VpnHood.AppLib.Dtos;
using VpnHood.AppLib.Settings;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.Core.Client.Devices;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.AppLib.WebServer.Client;

// IAppController over HTTP: the routes of AppController, one for one.
internal sealed class AppClient(HttpClient httpClient) : AppApiClientBase(httpClient), IAppController
{
    private const string BaseUrl = "api/app/";

    public Task ProcessTypes(ExceptionType exceptionType, SessionErrorCode errorCode, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This method exists just to let swagger generate types.");
    }

    public Task<AppData> Configure(ConfigParams configParams, CancellationToken cancellationToken)
    {
        return PatchAsync<ConfigParams, AppData>(BaseUrl + "configure", configParams, cancellationToken);
    }

    public Task<AppData> GetConfig(CancellationToken cancellationToken)
    {
        return GetAsync<AppData>(BaseUrl + "config", null, cancellationToken);
    }

    public Task<SplitIpsViaApp> GetSplitIpsViaApp(CancellationToken cancellationToken)
    {
        return GetAsync<SplitIpsViaApp>(BaseUrl + "split-by-ips-via-app", null, cancellationToken);
    }

    public Task SetSplitIpsViaApp(SplitIpsViaApp value, CancellationToken cancellationToken)
    {
        return PutBodyAsync(BaseUrl + "split-by-ips-via-app", value, cancellationToken);
    }

    public Task<SplitIpsViaDevice> GetSplitIpsViaDevice(CancellationToken cancellationToken)
    {
        return GetAsync<SplitIpsViaDevice>(BaseUrl + "split-by-ips-via-device", null, cancellationToken);
    }

    public Task SetSplitIpsViaDevice(SplitIpsViaDevice value, CancellationToken cancellationToken)
    {
        return PutBodyAsync(BaseUrl + "split-by-ips-via-device", value, cancellationToken);
    }

    public Task<SplitDomains> GetSplitDomains(CancellationToken cancellationToken)
    {
        return GetAsync<SplitDomains>(BaseUrl + "split-by-domains", null, cancellationToken);
    }

    public Task SetSplitDomains(SplitDomains value, CancellationToken cancellationToken)
    {
        return PutBodyAsync(BaseUrl + "split-by-domains", value, cancellationToken);
    }

    public Task<AppState> GetState(CancellationToken cancellationToken)
    {
        return GetAsync<AppState>(BaseUrl + "state", null, cancellationToken);
    }

    public Task Connect(Guid? clientProfileId, string? serverLocation, ConnectPlanId planId, CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "connect", ConnectQuery(clientProfileId, serverLocation, planId), cancellationToken);
    }

    public Task Diagnose(Guid? clientProfileId, string? serverLocation, ConnectPlanId planId, CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "diagnose", ConnectQuery(clientProfileId, serverLocation, planId), cancellationToken);
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
        return PostAsync(BaseUrl + "disconnect", null, cancellationToken);
    }

    public Task ClearLastError(CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "clear-last-error", null, cancellationToken);
    }

    public Task ClearReconnectRequired(CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "clear-reconnect-required", null, cancellationToken);
    }

    public Task SetUserSettings(UserSettings userSettings, CancellationToken cancellationToken)
    {
        return PutBodyAsync(BaseUrl + "user-settings", userSettings, cancellationToken);
    }

    public Task<string> Log(CancellationToken cancellationToken)
    {
        return GetStringAsync(BaseUrl + "log.txt", cancellationToken);
    }

    public Task<byte[]> PromotionImage(CancellationToken cancellationToken)
    {
        return GetBytesAsync(BaseUrl + "promotion.jpg", cancellationToken);
    }

    public Task<IReadOnlyList<DeviceAppInfo>> GetInstalledApps(CancellationToken cancellationToken)
    {
        return GetAsync<IReadOnlyList<DeviceAppInfo>>(BaseUrl + "installed-apps", null, cancellationToken);
    }

    public Task VersionCheck(CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "version-check", null, cancellationToken);
    }

    public Task VersionCheckPostpone(CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "version-check-postpone", null, cancellationToken);
    }

    public Task ExtendByRewardedAd(CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "extend-by-rewarded-ad", null, cancellationToken);
    }

    public Task SetUserReview(AppUserReview userReview, CancellationToken cancellationToken)
    {
        return PostBodyAsync(BaseUrl + "user-review", userReview, cancellationToken);
    }

    public Task<CountryInfo[]> GetCountries(CancellationToken cancellationToken)
    {
        return GetAsync<CountryInfo[]>(BaseUrl + "countries", null, cancellationToken);
    }

    public Task<CountryInfo[]> GetSupportedSplitCountries(CancellationToken cancellationToken)
    {
        return GetAsync<CountryInfo[]>(BaseUrl + "supported-split-by-countries", null, cancellationToken);
    }

    public Task InternalAdDismiss(ShowAdResult result, CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "internal-ad/dismiss", new Dictionary<string, object?> { ["result"] = result }, cancellationToken);
    }

    public Task InternalAdError(string errorMessage, CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "internal-ad/error", new Dictionary<string, object?> { ["errorMessage"] = errorMessage }, cancellationToken);
    }

    public Task<RemoteAccessState> GetRemoteAccess(CancellationToken cancellationToken)
    {
        return GetAsync<RemoteAccessState>(BaseUrl + "remote-access", null, cancellationToken);
    }

    public Task<RemoteAccessState> StartRemoteAccess(CancellationToken cancellationToken)
    {
        return PostAsync<RemoteAccessState>(BaseUrl + "remote-access/start", null, cancellationToken);
    }

    public Task StopRemoteAccess(CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "remote-access/stop", null, cancellationToken);
    }
}
