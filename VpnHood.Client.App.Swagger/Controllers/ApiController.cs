using Microsoft.AspNetCore.Mvc;
using VpnHood.Client.App.Settings;
using VpnHood.Client.App.WebServer.Api;
using VpnHood.Client.Device;

namespace VpnHood.Client.App.Swagger.Controllers
{
    [ApiController]
    [Route("api")]
    public class ApiController : ControllerBase, IClientApi
    {
        [HttpPost(nameof(addAccessKey))]
        public Task<ClientProfile> addAccessKey(AddClientProfileParam addClientProfileParam)
        {
            throw new NotImplementedException();
        }

        [HttpGet("/app/config")]
        public Task<AppConfig> GetConfig()
        {
            throw new NotImplementedException();
        }

        [HttpGet("/app/state")]
        public Task<AppState> GetState()
        {
            throw new NotImplementedException();
        }

        [HttpPost(nameof(connect))]
        public Task connect(ConnectParam connectParam)
        {
            throw new NotImplementedException();
        }

        [HttpPost(nameof(diagnose))]
        public Task diagnose(ConnectParam connectParam)
        {
            throw new NotImplementedException();
        }

        [HttpPost(nameof(disconnect))]
        public Task disconnect()
        {
            throw new NotImplementedException();
        }

        [HttpPost(nameof(setClientProfile))]
        public Task setClientProfile(SetClientProfileParam setClientProfileParam)
        {
            throw new NotImplementedException();
        }

        [HttpPost(nameof(removeClientProfile))]
        public Task removeClientProfile(RemoveClientProfileParam removeClientProfileParam)
        {
            throw new NotImplementedException();
        }

        [HttpPost(nameof(clearLastError))]
        public void clearLastError()
        {
            throw new NotImplementedException();
        }

        [HttpPost(nameof(addTestServer))]
        public void addTestServer()
        {
            throw new NotImplementedException();
        }

        [HttpPost(nameof(setUserSettings))]
        public Task setUserSettings(UserSettings userSettings)
        {
            throw new NotImplementedException();
        }

        [HttpGet(nameof(log))]
        public Task log()
        {
            throw new NotImplementedException();
        }

        [HttpPost(nameof(installedApps))]
        public Task<DeviceAppInfo[]> installedApps()
        {
            throw new NotImplementedException();
        }

        [HttpPost(nameof(ipGroups))]
        public Task<IpGroup[]> ipGroups()
        {
            throw new NotImplementedException();
        }
    }
}