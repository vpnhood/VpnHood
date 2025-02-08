using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Settings;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Common.Utils;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class AppController : WebApiController, IAppController
{
    private static VpnHoodApp App => VpnHoodApp.Instance;


    [Route(HttpVerbs.Patch, "/configure")]
    public async Task<AppData> Configure(ConfigParams configParams)
    {
        configParams = await HttpContext.GetRequestDataAsync<ConfigParams>().VhConfigureAwait();
        App.Services.CultureProvider.AvailableCultures = configParams.AvailableCultures;
        if (configParams.Strings != null) App.Resource.Strings = configParams.Strings;

        App.UpdateUi();
        return await GetConfig().VhConfigureAwait();
    }

    [Route(HttpVerbs.Get, "/config")]
    public Task<AppData> GetConfig()
    {
        var ret = new AppData {
            Features = App.Features,
            Settings = App.Settings,
            ClientProfileInfos = App.ClientProfileService.List().Select(x => x.ToInfo()).ToArray(),
            State = App.State,
            AvailableCultureInfos = App.Services.CultureProvider.AvailableCultures
                .Select(x => new UiCultureInfo(x))
                .ToArray()
        };

        return Task.FromResult(ret);
    }

    [Route(HttpVerbs.Get, "/ip-filters")]
    public Task<IpFilters> GetIpFilters()
    {
        var appIpFilters = new IpFilters {
            VpnAdapterIpFilterInclude = App.SettingsService.IpFilterSettings.VpnAdapterIpFilterIncludes,
            VpnAdapterIpFilterExclude = App.SettingsService.IpFilterSettings.VpnAdapterIpFilterExcludes,
            AppIpFilterInclude = App.SettingsService.IpFilterSettings.AppIpFilterIncludes,
            AppIpFilterExclude = App.SettingsService.IpFilterSettings.AppIpFilterExcludes
        };

        return Task.FromResult(appIpFilters);
    }

    [Route(HttpVerbs.Put, "/ip-filters")]
    public async Task SetIpFilters(IpFilters ipFilters)
    {
        ipFilters = await HttpContext.GetRequestDataAsync<IpFilters>().VhConfigureAwait();
        App.SettingsService.IpFilterSettings.VpnAdapterIpFilterExcludes = ipFilters.VpnAdapterIpFilterExclude;
        App.SettingsService.IpFilterSettings.VpnAdapterIpFilterIncludes = ipFilters.VpnAdapterIpFilterInclude;
        App.SettingsService.IpFilterSettings.AppIpFilterExcludes = ipFilters.AppIpFilterExclude;
        App.SettingsService.IpFilterSettings.AppIpFilterIncludes = ipFilters.AppIpFilterInclude;
    }

    [Route(HttpVerbs.Get, "/state")]
    public Task<AppState> GetState()
    {
        return Task.FromResult(App.State);
    }

    [Route(HttpVerbs.Post, "/connect")]
    public Task Connect([QueryField] Guid? clientProfileId = null, [QueryField] string? serverLocation = null,
        [QueryField] ConnectPlanId planId = ConnectPlanId.Normal)
    {
        return App.Connect(
            clientProfileId,
            serverLocation: serverLocation,
            diagnose: false,
            userAgent: HttpContext.Request.UserAgent,
            planId: planId);
    }

    [Route(HttpVerbs.Post, "/diagnose")]
    public Task Diagnose([QueryField] Guid? clientProfileId = null, [QueryField] string? serverLocation = null,
        [QueryField] ConnectPlanId planId = ConnectPlanId.Normal)
    {
        return App.Connect(
            clientProfileId,
            serverLocation: serverLocation,
            diagnose: true,
            planId: planId,
            userAgent: HttpContext.Request.UserAgent);
    }

    [Route(HttpVerbs.Post, "/disconnect")]
    public Task Disconnect()
    {
        return App.Disconnect(true);
    }

    [Route(HttpVerbs.Post, "/version-check")]
    public Task VersionCheck()
    {
        return App.VersionCheck(true);
    }

    [Route(HttpVerbs.Post, "/version-check-postpone")]
    public void VersionCheckPostpone()
    {
        App.VersionCheckPostpone();
    }

    [Route(HttpVerbs.Post, "/clear-last-error")]
    public void ClearLastError()
    {
        App.ClearLastError();
    }

    [Route(HttpVerbs.Post, "/extend-by-rewarded-ad")]
    public Task ExtendByRewardedAd()
    {
        return App.ExtendByRewardedAd(HttpContext.CancellationToken);
    }

    [Route(HttpVerbs.Put, "/user-settings")]
    public async Task SetUserSettings(UserSettings userSettings)
    {
        userSettings = await HttpContext.GetRequestDataAsync<UserSettings>().VhConfigureAwait();
        App.SettingsService.AppSettings.UserSettings = userSettings;
        App.SettingsService.Save();
    }

    [Route(HttpVerbs.Get, "/log.txt")]
    public async Task<string> Log()
    {
        Response.ContentType = MimeType.PlainText;
        await using var stream = HttpContext.OpenResponseStream();
        await using var streamWriter = new StreamWriter(stream);
        var log = await App.LogService.GetLog().VhConfigureAwait();
        await streamWriter.WriteAsync(log).VhConfigureAwait();
        HttpContext.SetHandled();
        return "";
    }

    [Route(HttpVerbs.Get, "/installed-apps")]
    public Task<DeviceAppInfo[]> GetInstalledApps()
    {
        return Task.FromResult(App.Device.InstalledApps);
    }

    [Route(HttpVerbs.Post, "/settings/open-always-on-page")]
    public void OpenAlwaysOnPage()
    {
        App.Services.UiProvider.OpenAlwaysOnPage(ActiveUiContext.RequiredContext);
    }

    [Route(HttpVerbs.Post, "/settings/request-quick-launch")]
    public Task RequestQuickLaunch()
    {
        return App.Services.UiProvider.RequestQuickLaunch(ActiveUiContext.RequiredContext, CancellationToken.None);
    }

    [Route(HttpVerbs.Post, "/settings/request-notification")]
    public Task RequestNotification()
    {
        return App.Services.UiProvider.RequestNotification(ActiveUiContext.RequiredContext, CancellationToken.None);
    }
}