using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

#pragma warning disable IDE1006 // Naming Styles
namespace VpnHood.Client.App.UI
{
    internal class ApiController : WebApiController
    {
        private VpnHoodApp App => VpnHoodApp.Current;

        class LoadAppParam
        {
            public bool WithFeatures { get; set; }
            public bool WithState { get; set; }
            public bool WithSettings { get; set; }
            public bool WithClientProfileItems { get; set; }
        }

        public class LoadAppResult
        {
            public AppFeatures Features { get; set; }
            public AppSettings Settings { get; set; }
            public AppState State { get; set; }
            public ClientProfileItem[] ClientProfileItems { get; set; }
        }

        [Route(HttpVerbs.Post, "/" + nameof(loadApp))]
        public async Task<LoadAppResult> loadApp()
        {
            var parameters = await GetRequestDataAsync<LoadAppParam>();
            var ret = new LoadAppResult()
            {
                Features = parameters.WithFeatures ? App.Features : null,
                State = parameters.WithState ? App.State : null,
                Settings = parameters.WithSettings ? App.Settings : null,
                ClientProfileItems = parameters.WithClientProfileItems ? App.ClientProfileStore.ClientProfileItems : null
            };
            return ret;
        }

        class AddClientProfileParam
        {
            public string AccessKey { get; set; }
        }

        [Route(HttpVerbs.Post, "/" + nameof(addAccessKey))]
        public async Task<ClientProfile> addAccessKey()
        {
            var parameters = await GetRequestDataAsync<AddClientProfileParam>();
            var clientProfile = App.ClientProfileStore.AddAccessKey(parameters.AccessKey);
            App.Settings.UserSettings.DefaultClientProfileId = clientProfile.ClientProfileId;
            return clientProfile;
        }

        class ConnectParam
        {
            public Guid ClientProfileId { get; set; }
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
            await App.Connect(parameters.ClientProfileId, diagnose: true, userAgent: HttpContext.Request.UserAgent);
        }

        [Route(HttpVerbs.Post, "/" + nameof(disconnect))]
        public void disconnect()
        {
            App.Disconnect(true);
        }

        class RemoveClientProfileParam
        {
            public Guid ClientProfileId { get; set; }
        }

        [Route(HttpVerbs.Post, "/" + nameof(removeClientProfile))]
        public async Task removeClientProfile()
        {
            var parameters = await GetRequestDataAsync<RemoveClientProfileParam>();
            if (parameters.ClientProfileId == App.ActiveClientProfile?.ClientProfileId)
                App.Disconnect(true);
            App.ClientProfileStore.RemoveClientProfile(parameters.ClientProfileId);
        }

        class SetClientProfileParam
        {
            public ClientProfile ClientProfile { get; set; }
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
            using var stream = HttpContext.OpenResponseStream();
            using var StreamWriter = new StreamWriter(stream);
            var log = App.GetLogForReport();
            await StreamWriter.WriteAsync(log);
        }

        [Route(HttpVerbs.Post, "/" + nameof(installedApps))]
        public Device.DeviceAppInfo[] installedApps()
        {
            return App.Device.InstalledApps;
        }

        [Route(HttpVerbs.Post, "/" + nameof(ipGroups))]
        public IpGroup[] ipGroups()
        {
            return App.IpGroups;
        }

        private Task<TData> GetRequestDataAsync<TData>()
        {
            return HttpContext.GetRequestDataAsync(async context =>
            {
                var data = await context.GetRequestBodyAsStringAsync();
                return JsonSerializer.Deserialize<TData>(data, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            });
        }

    }
}
