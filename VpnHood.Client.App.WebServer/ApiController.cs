using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
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


    [Route(HttpVerbs.Post, "/" + nameof (loadApp))]
    public async Task<LoadAppResponse> loadApp(LoadAppParam loadAppParam)
    {
        loadAppParam = await GetRequestDataAsync<LoadAppParam>();
        var ret = new LoadAppResponse
        {
            Features = loadAppParam.WithFeatures ? App.Features : null,
            State = loadAppParam.WithState ? App.State : null,
            Settings = loadAppParam.WithSettings ? App.Settings : null,
            ClientProfileItems =
                loadAppParam.WithClientProfileItems ? App.ClientProfileStore.ClientProfileItems : null
        };
        return ret;
    }

    [Route(HttpVerbs.Post, "/" + nameof(addAccessKey))]
    public async Task<ClientProfile> addAccessKey(AddClientProfileParam addClientProfileParam)
    {
        addClientProfileParam = await GetRequestDataAsync<AddClientProfileParam>();
        var clientProfile = App.ClientProfileStore.AddAccessKey(addClientProfileParam.AccessKey);
        App.Settings.UserSettings.DefaultClientProfileId = clientProfile.ClientProfileId;
        return clientProfile;
    }

    [Route(HttpVerbs.Post, "/" + nameof(connect))]
    public async Task connect(ConnectParam connectParam)
    {
        connectParam = await GetRequestDataAsync<ConnectParam>();
        await App.Connect(connectParam.ClientProfileId, userAgent: HttpContext.Request.UserAgent);
    }

    [Route(HttpVerbs.Post, "/" + nameof(diagnose))]
    public async Task diagnose(ConnectParam connectParam)
    {
        connectParam = await GetRequestDataAsync<ConnectParam>();
        await App.Connect(connectParam.ClientProfileId, true, HttpContext.Request.UserAgent);
    }

    [Route(HttpVerbs.Post, "/" + nameof(disconnect))]
    public async Task disconnect()
    {
        await App.Disconnect(true);
    }

    [Route(HttpVerbs.Post, "/" + nameof(removeClientProfile))]
    public async Task removeClientProfile(RemoveClientProfileParam removeClientProfileParam)
    {
        removeClientProfileParam = await GetRequestDataAsync<RemoveClientProfileParam>();
        if (removeClientProfileParam.ClientProfileId == App.ActiveClientProfile?.ClientProfileId)
            await App.Disconnect(true);
        App.ClientProfileStore.RemoveClientProfile(removeClientProfileParam.ClientProfileId);
    }

    [Route(HttpVerbs.Post, "/" + nameof(setClientProfile))]
    public async Task setClientProfile(SetClientProfileParam setClientProfileParam)
    {
        setClientProfileParam = await GetRequestDataAsync<SetClientProfileParam>();
        App.ClientProfileStore.SetClientProfile(setClientProfileParam.ClientProfile);
    }

    [Route(HttpVerbs.Post, "/" + nameof(clearLastError))]
    public void clearLastError()
    {
        App.ClearLastError();
    }

    [Route(HttpVerbs.Post, "/" + nameof(addTestServer))]
    public void addTestServer()
    {
        App.ClientProfileStore.AddAccessKey(App.Settings.TestServerAccessKey);
    }

    [Route(HttpVerbs.Post, "/" + nameof(setUserSettings))]
    public async Task setUserSettings(UserSettings userSettings)
    {
        userSettings = await GetRequestDataAsync<UserSettings>();
        App.Settings.UserSettings = userSettings;
        App.Settings.Save();
    }

    [Route(HttpVerbs.Get, "/log.txt")]
    public async Task log()
    {
        Response.ContentType = MimeType.PlainText;
        await using var stream = HttpContext.OpenResponseStream();
        await using var streamWriter = new StreamWriter(stream);
        var log = await App.LogService.GetLog();
        await streamWriter.WriteAsync(log);
    }

    [Route(HttpVerbs.Post, "/" + nameof(installedApps))]
    public Task<DeviceAppInfo[]> installedApps()
    {
        return Task.FromResult(App.Device.InstalledApps);
    }

    [Route(HttpVerbs.Post, "/" + nameof(ipGroups))]
    public Task<IpGroup[]> ipGroups()
    {
        return App.GetIpGroups();
    }

    private async Task<T> GetRequestDataAsync<T>()
    {
        var json = await HttpContext.GetRequestBodyAsByteArrayAsync();
        var res = JsonSerializer.Deserialize<T>(json,
            new JsonSerializerOptions {PropertyNamingPolicy = JsonNamingPolicy.CamelCase});
        if (res == null)
            throw new Exception($"The request expected to have a {typeof(T).Name} but it is null!");
        return res;
    }
   
}