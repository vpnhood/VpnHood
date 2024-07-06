using System.Text.Json;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VpnHood.Client.App.ClientProfiles;
using VpnHood.Client.App.Settings;
using VpnHood.Client.App.WebServer.Api;
using VpnHood.Client.Device;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.WebServer.Controllers;

internal class AppController : WebApiController, IAppController
{
    private static VpnHoodApp App => VpnHoodApp.Instance;
    private async Task<T> GetRequestDataAsync<T>()
    {
        var json = await HttpContext.GetRequestBodyAsByteArrayAsync().VhConfigureAwait();
        var res = JsonSerializer.Deserialize<T>(json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        
        return res ?? throw new Exception($"The request expected to have a {typeof(T).Name} but it is null!");
    }

    [Route(HttpVerbs.Patch, "/configure")]
    public async Task<AppConfig> Configure(ConfigParams configParams)
    {
        configParams = await GetRequestDataAsync<ConfigParams>().VhConfigureAwait();
        App.Services.AppCultureService.AvailableCultures = configParams.AvailableCultures;
        if (configParams.Strings != null) App.Resource.Strings = configParams.Strings;

        App.UpdateUi();
        return await GetConfig().VhConfigureAwait();
    }

    [Route(HttpVerbs.Get, "/config")]
    public Task<AppConfig> GetConfig()
    {
        var ret = new AppConfig
        {
            Features = App.Features,
            Settings = App.Settings,
            ClientProfileInfos = App.ClientProfileService.List().Select(x => x.ToInfo()).ToArray(),
            State = App.State,
            AvailableCultureInfos = App.Services.AppCultureService.AvailableCultures
                .Select(x => new UiCultureInfo(x))
                .ToArray()
        };

        return Task.FromResult(ret);

    }

    [Route(HttpVerbs.Get, "/state")]
    public Task<AppState> GetState()
    {
        return Task.FromResult(App.State);
    }

    [Route(HttpVerbs.Post, "/connect")]
    public Task Connect([QueryField] Guid? clientProfileId = null, [QueryField] string? serverLocation = null)
    {
        return App.Connect(clientProfileId, serverLocation: serverLocation, diagnose: false,
            userAgent: HttpContext.Request.UserAgent, throwException: false);
    }

    [Route(HttpVerbs.Post, "/diagnose")]
    public Task Diagnose([QueryField] Guid? clientProfileId = null,[QueryField] string? serverLocation = null)
    {
        return App.Connect(clientProfileId, serverLocation: serverLocation, diagnose: true,
            userAgent: HttpContext.Request.UserAgent, throwException: false);
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

    [Route(HttpVerbs.Put, "/access-keys")]
    public Task<ClientProfileInfo> AddAccessKey([QueryField] string accessKey)
    {
        var clientProfile = App.ClientProfileService.ImportAccessKey(accessKey);
        return Task.FromResult(clientProfile.ToInfo());
    }

    [Route(HttpVerbs.Post, "/clear-last-error")]
    public void ClearLastError()
    {
        App.ClearLastError();
    }

    [Route(HttpVerbs.Put, "/user-settings")]
    public async Task SetUserSettings(UserSettings userSettings)
    {
        userSettings = await GetRequestDataAsync<UserSettings>().VhConfigureAwait();
        App.Settings.UserSettings = userSettings;
        App.Settings.Save();
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

    [Route(HttpVerbs.Get, "/ip-groups")]
    public async Task<IpGroupInfo[]> GetIpGroups()
    {
        var ipGroupManager = await App.GetIpGroupManager().VhConfigureAwait();
        var ipGroupIds = await ipGroupManager.GetIpGroupIds().VhConfigureAwait();
        return ipGroupIds.Select(x=>new IpGroupInfo{IpGroupId = x}).ToArray();
    }

    [Route(HttpVerbs.Patch, "/client-profiles/{clientProfileId}")]
    public async Task<ClientProfileInfo> UpdateClientProfile(Guid clientProfileId, ClientProfileUpdateParams updateParams)
    {
        updateParams = await GetRequestDataAsync<ClientProfileUpdateParams>().VhConfigureAwait();
        var clientProfile = App.ClientProfileService.Update(clientProfileId, updateParams);
        return clientProfile.ToInfo();
    }

    [Route(HttpVerbs.Delete, "/client-profiles/{clientProfileId}")]
    public async Task DeleteClientProfile(Guid clientProfileId)
    {
        if (clientProfileId == App.CurrentClientProfile?.ClientProfileId)
            await App.Disconnect(true).VhConfigureAwait();

        App.ClientProfileService.Remove(clientProfileId);
    }

    [Route(HttpVerbs.Post, "/settings/open-always-on-page")]
    public void OpenAlwaysOnPage()
    {
        App.Services.UiService.OpenAlwaysOnPage(ActiveUiContext.RequiredContext);
    }

    [Route(HttpVerbs.Post, "/settings/request-quick-launch")]
    public Task RequestQuickLaunch()
    {
        return App.Services.UiService.RequestQuickLaunch(ActiveUiContext.RequiredContext, CancellationToken.None);
    }

    [Route(HttpVerbs.Post, "/settings/request-notification")]
    public Task RequestNotification()
    {
        return App.Services.UiService.RequestNotification(ActiveUiContext.RequiredContext, CancellationToken.None);
    }

}