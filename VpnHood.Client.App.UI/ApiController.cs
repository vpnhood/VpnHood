using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VpnHood.Client.Device;

// ReSharper disable InconsistentNaming
// ReSharper disable ClassNeverInstantiated.Local
// ReSharper disable UnusedAutoPropertyAccessor.Local
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Local

#pragma warning disable IDE1006 // Naming Styles
namespace VpnHood.Client.App.UI;

internal class ApiController : WebApiController
{
    private static VpnHoodApp App => VpnHoodApp.Instance;

    [Route(HttpVerbs.Post, "/" + nameof(loadApp))]
    public async Task<LoadAppResult> loadApp()
    {
        var parameters = await GetRequestDataAsync<LoadAppParam>();
        var ret = new LoadAppResult
        {
            Features = parameters.WithFeatures ? App.Features : null,
            State = parameters.WithState ? App.State : null,
            Settings = parameters.WithSettings ? App.Settings : null,
            ClientProfileItems =
                parameters.WithClientProfileItems ? App.ClientProfileStore.ClientProfileItems : null
        };
        return ret;
    }

    [Route(HttpVerbs.Post, "/" + nameof(addAccessKey))]
    public async Task<ClientProfile> addAccessKey()
    {
        var parameters = await GetRequestDataAsync<AddClientProfileParam>();
        var clientProfile = App.ClientProfileStore.AddAccessKey(parameters.AccessKey);
        App.Settings.UserSettings.DefaultClientProfileId = clientProfile.ClientProfileId;
        return clientProfile;
    }

    [Route(HttpVerbs.Post, "/" + nameof(connect))]
    public async Task connect()
    {
        var parameters = await GetRequestDataAsync<ConnectParam>();
        await App.Connect(parameters.ClientProfileId, userAgent: HttpContext.Request.UserAgent);
    }

    [Route(HttpVerbs.Post, "/" + nameof(diagnose))]
    public async Task diagnose()
    {
        var parameters = await GetRequestDataAsync<ConnectParam>();
        await App.Connect(parameters.ClientProfileId, true, HttpContext.Request.UserAgent);
    }

    [Route(HttpVerbs.Post, "/" + nameof(disconnect))]
    public async Task disconnect()
    {
        await App.Disconnect(true);
    }

    [Route(HttpVerbs.Post, "/" + nameof(removeClientProfile))]
    public async Task removeClientProfile()
    {
        var parameters = await GetRequestDataAsync<RemoveClientProfileParam>();
        if (parameters.ClientProfileId == App.ActiveClientProfile?.ClientProfileId)
            await App.Disconnect(true);
        App.ClientProfileStore.RemoveClientProfile(parameters.ClientProfileId);
    }

    [Route(HttpVerbs.Post, "/" + nameof(setClientProfile))]
    public async Task setClientProfile()
    {
        var parameters = await GetRequestDataAsync<SetClientProfileParam>();
        App.ClientProfileStore.SetClientProfile(parameters.ClientProfile);
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
    public async Task setUserSettings()
    {
        var parameters = await GetRequestDataAsync<UserSettings>();
        App.Settings.UserSettings = parameters;
        App.Settings.Save();
    }

    [Route(HttpVerbs.Get, "/log.txt")]
    public async Task log()
    {
        Response.ContentType = MimeType.PlainText;
        await using var stream = HttpContext.OpenResponseStream();
        await using var StreamWriter = new StreamWriter(stream);
        var log = App.GetLogForReport();
        await StreamWriter.WriteAsync(log);
    }

    [Route(HttpVerbs.Post, "/" + nameof(installedApps))]
    public DeviceAppInfo[] installedApps()
    {
        return App.Device.InstalledApps;
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

    private class LoadAppParam
    {
        public bool WithFeatures { get; set; }
        public bool WithState { get; set; }
        public bool WithSettings { get; set; }
        public bool WithClientProfileItems { get; set; }
    }

    public class LoadAppResult
    {
        public AppFeatures? Features { get; set; }
        public AppSettings? Settings { get; set; }
        public AppState? State { get; set; }
        public ClientProfileItem[]? ClientProfileItems { get; set; }
    }

    private class AddClientProfileParam
    {
        public string AccessKey { get; set; } = null!;
    }

    private class ConnectParam
    {
        public Guid ClientProfileId { get; set; }
    }

    private class RemoveClientProfileParam
    {
        public Guid ClientProfileId { get; set; }
    }

    private class SetClientProfileParam
    {
        public ClientProfile ClientProfile { get; set; } = null!;
    }
}