using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using Swan;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

#pragma warning disable IDE1006 // Naming Styles
namespace VpnHood.Client.App.UI
{
    internal class ApiController : WebApiController
    {
        private VpnHoodApp App => VpnHoodApp.Current;

        #region *** Api: loadApp 
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
            public AppClientProfileItem[] ClientProfileItems { get; set; }
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
        #endregion

        #region *** Api: addAccessKey
        class AddClientProfileParam
        {
            public string AccessKey { get; set; }
        }

        [Route(HttpVerbs.Post, "/addAccessKey")]
        public async Task<AppClientProfile> AddAccessKey()
        {
            var parameters = await GetRequestDataAsync<AddClientProfileParam>();
            return App.ClientProfileStore.AddAccessKey(parameters.AccessKey);
        }
        #endregion

        #region *** Api: connect
        class ConnectParam
        {
            public Guid ClientProfileId { get; set; }
        }
        [Route(HttpVerbs.Post, "/" + nameof(connect))]
        public async Task connect()
        {
            var parameters = await GetRequestDataAsync<ConnectParam>();
            App.Connect(parameters.ClientProfileId);
        }
        #endregion

        #region *** Api: disconnect
        [Route(HttpVerbs.Post, "/" + nameof(disconnect))]
        public void disconnect()
        {
            App.Disconnect();
        }
        #endregion

        #region *** Api: removeClientProfile
        class RemoveClientProfileParam
        {
            public Guid ClientProfileId { get; set; }
        }
        [Route(HttpVerbs.Post, "/" + nameof(removeClientProfile))]
        public async Task removeClientProfile()
        {
            var parameters = await GetRequestDataAsync<RemoveClientProfileParam>();
            if (parameters.ClientProfileId == App.ActiveClientProfile?.ClientProfileId)
                App.Disconnect();
            App.ClientProfileStore.RemoveClientProfile(parameters.ClientProfileId);
        }
        #endregion

        #region *** Api: setClientProfile
        class SetClientProfileParam
        {
            public AppClientProfile ClientProfile { get; set; }
        }
        [Route(HttpVerbs.Post, "/" + nameof(setClientProfile))]
        public async Task setClientProfile()
        {
            var parameters = await GetRequestDataAsync<SetClientProfileParam>();
            App.ClientProfileStore.SetClientProfile(parameters.ClientProfile);
        }
        #endregion

        public Task<TData> GetRequestDataAsync<TData>()
        {
            return HttpContext.GetRequestDataAsync(async context =>
            {
                var data = await context.GetRequestBodyAsStringAsync();
                return JsonSerializer.Deserialize<TData>(data, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            });
        }

    }
}
