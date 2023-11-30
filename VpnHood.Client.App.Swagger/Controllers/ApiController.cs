using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using VpnHood.Client.App.Settings;
using VpnHood.Client.App.WebServer.Api;
using VpnHood.Client.Device;

namespace VpnHood.Client.App.Swagger.Controllers
{
    [ApiController]
    [Route("api/app")]
    public class ApiController : ControllerBase, IClientApi
    {
        [HttpGet("config")]
        public Task<AppConfig> GetConfig()
        {
            throw new NotImplementedException();
        }

        [HttpGet("state")]
        public Task<AppState> GetState()
        {
            throw new NotImplementedException();
        }

        [HttpPost("connect")]
        public Task Connect(Guid? clientProfileId = null)
        {
            throw new NotImplementedException();
        }

        [HttpPost("diagnose")]
        public Task Diagnose(Guid? clientProfileId = null)
        {
            throw new NotImplementedException();
        }

        [HttpPost("disconnect")]
        public Task Disconnect()
        {
            throw new NotImplementedException();
        }

        [HttpPut("access-keys")]
        public Task<ClientProfile> AddAccessKey(string accessKey)
        {
            throw new NotImplementedException();
        }

        [HttpPost("clear-last-error")]
        public void ClearLastError()
        {
            throw new NotImplementedException();
        }

        [HttpPost("add-test-server")]
        public void AddTestServer()
        {
            throw new NotImplementedException();
        }

        [HttpPut("user-settings")]
        public Task SetUserSettings(UserSettings userSettings)
        {
            throw new NotImplementedException();
        }

        [HttpGet("log.txt")]
        [Produces(MediaTypeNames.Text.Plain)]
        public Task<string> Log()
        {
            throw new NotImplementedException();
        }

        [HttpGet("installed-apps")]
        public Task<DeviceAppInfo[]> GetInstalledApps()
        {
            throw new NotImplementedException();
        }

        [HttpGet("ip-groups")]
        public Task<IpGroup[]> GetIpGroups()
        {
            throw new NotImplementedException();
        }

        [HttpPatch("client-profiles/{clientProfileId}")]
        public Task UpdateClientProfile(Guid clientProfileId, ClientProfileUpdateParams updateParams)
        {
            throw new NotImplementedException();
        }

        [HttpDelete("client-profiles/{clientProfileId}")]
        public Task DeleteClientProfile(Guid clientProfileId)
        {
            throw new NotImplementedException();
        }
    }
}