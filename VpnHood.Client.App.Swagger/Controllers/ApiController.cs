using Microsoft.AspNetCore.Mvc;
using VpnHood.Client.App.Settings;
using VpnHood.Client.App.UI.Api;

namespace VpnHood.Client.App.Swagger.Controllers
{
    [ApiController]
    [Route("api")]
    public class ApiController : ControllerBase, IClientApi
    {
        [HttpPost(nameof(loadApp))]
        public Task<LoadAppResponse> loadApp(LoadAppParam loadAppParam)
        {
            throw new NotImplementedException();
        }

        [HttpPost(nameof(addAccessKey))]
        public Task<ClientProfile> addAccessKey(AddClientProfileParam addClientProfileParam)
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

        [HttpPost(nameof(log))]
        public Task log()
        {
            throw new NotImplementedException();
        }
    }
}