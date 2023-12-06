using System.Text.Json;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VpnHood.Client.App.Settings;
using VpnHood.Client.App.WebServer.Api;
using VpnHood.Client.Device;

namespace VpnHood.Client.App.WebServer;

internal class ClientApiController : WebApiController, IClientApi
{
    private static VpnHoodApp App => VpnHoodApp.Instance;

    [Route(HttpVerbs.Get, "/config")]
    public Task<AppConfig> GetConfig()
    {
        var ret = new AppConfig
        {
            Features = App.Features,
            Settings = App.Settings,
            ClientProfileItems = App.ClientProfileStore.ClientProfileItems,
            State = App.State
        };

        return Task.FromResult(ret);
    }

    [Route(HttpVerbs.Get, "/state")]
    public Task<AppState> GetState()
    {
        return Task.FromResult(App.State);
    }

    [Route(HttpVerbs.Post, "/connect")]
    public Task Connect([QueryField] Guid? clientProfileId = null)
    {
        return App.Connect(clientProfileId, userAgent: HttpContext.Request.UserAgent, throwException: false);
    }

    [Route(HttpVerbs.Post, "/diagnose")]
    public Task Diagnose([QueryField] Guid? clientProfileId = null)
    {
        return App.Connect(clientProfileId, true, HttpContext.Request.UserAgent, throwException: false);
    }

    [Route(HttpVerbs.Post, "/disconnect")]
    public Task Disconnect()
    {
        return App.Disconnect(true);
    }

    [Route(HttpVerbs.Put, "/access-keys")]
    public Task<ClientProfile> AddAccessKey([QueryField] string accessKey)
    {
        var clientProfile = App.ClientProfileStore.AddAccessKey(accessKey);
        return Task.FromResult(clientProfile);
    }

    [Route(HttpVerbs.Post, "/clear-last-error")]
    public void ClearLastError()
    {
        App.ClearLastError();
    }

    [Route(HttpVerbs.Post, "/add-test-server")]
    public void AddTestServer()
    {
        App.ClientProfileStore.AddAccessKey(App.Settings.TestServerAccessKey);
    }

    [Route(HttpVerbs.Put, "/user-settings")]
    public async Task SetUserSettings(UserSettings userSettings)
    {
        userSettings = await GetRequestDataAsync<UserSettings>();
        App.Settings.UserSettings = userSettings;
        App.Settings.Save();
    }

    [Route(HttpVerbs.Get, "/log.txt")]
    public async Task<string> Log()
    {
        Response.ContentType = MimeType.PlainText;
        await using var stream = HttpContext.OpenResponseStream();
        await using var streamWriter = new StreamWriter(stream);
        var log = await App.LogService.GetLog();
        await streamWriter.WriteAsync(log);
        return "";
    }

    [Route(HttpVerbs.Get, "/installed-apps")]
    public Task<DeviceAppInfo[]> GetInstalledApps()
    {
        return Task.FromResult(App.Device.InstalledApps);
    }

    [Route(HttpVerbs.Get, "/ip-groups")]
    public Task<IpGroup[]> GetIpGroups()
    {
        return App.GetIpGroups();
    }

    [Route(HttpVerbs.Patch, "/client-profiles/{clientProfileId}")]
    public async Task UpdateClientProfile(Guid clientProfileId, ClientProfileUpdateParams updateParams)
    {
        updateParams = await GetRequestDataAsync<ClientProfileUpdateParams>();

        var clientProfileItem = App.ClientProfileStore.ClientProfileItems.Single(x => x.ClientProfileId == clientProfileId);
        if (updateParams.Name != null)
            clientProfileItem.ClientProfile.Name = string.IsNullOrEmpty(updateParams.Name) ? null : updateParams.Name;

        App.ClientProfileStore.SetClientProfile(clientProfileItem.ClientProfile);
    }

    [Route(HttpVerbs.Delete, "/client-profiles/{clientProfileId}")]
    public async Task DeleteClientProfile(Guid clientProfileId)
    {
        if (clientProfileId == App.ActiveClientProfile?.ClientProfileId)
            await App.Disconnect(true);
        App.ClientProfileStore.RemoveClientProfile(clientProfileId);
    }

    private async Task<T> GetRequestDataAsync<T>()
    {
        var json = await HttpContext.GetRequestBodyAsByteArrayAsync();
        var res = JsonSerializer.Deserialize<T>(json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        if (res == null)
            throw new Exception($"The request expected to have a {typeof(T).Name} but it is null!");
        return res;
    }

}