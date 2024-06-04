using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using VpnHood.Client.App.ClientProfiles;
using VpnHood.Client.App.Settings;
using VpnHood.Client.App.WebServer.Api;
using VpnHood.Client.Device;

namespace VpnHood.Client.App.Swagger.Controllers;

[ApiController]
[Route("api/app")]
public class AppController : ControllerBase, IAppController
{
    [HttpPatch("configure")]
    public Task<AppConfig> Configure(ConfigParams configParams)
    {
        throw new NotImplementedException();
    }

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
    public Task Connect(Guid? clientProfileId = null, string? serverLocation = null)
    {
        throw new NotImplementedException();
    }

    [HttpPost("diagnose")]
    public Task Diagnose(Guid? clientProfileId = null, string? serverLocation = null)
    {
        throw new NotImplementedException();
    }

    [HttpPost("disconnect")]
    public Task Disconnect()
    {
        throw new NotImplementedException();
    }

    [HttpPost("clear-last-error")]
    public void ClearLastError()
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
    public Task<IpGroupInfo[]> GetIpGroups()
    {
        throw new NotImplementedException();
    }

    [HttpPost("version-check")]
    public Task VersionCheck()
    {
        throw new NotImplementedException();
    }

    [HttpPost("version-check-postpone")]
    public void VersionCheckPostpone()
    {
        throw new NotImplementedException();
    }

    [HttpPut("access-keys")]
    public Task<ClientProfileInfo> AddAccessKey(string accessKey)
    {
        throw new NotImplementedException();
    }

    [HttpPatch("client-profiles/{clientProfileId:guid}")]
    public Task<ClientProfileInfo> UpdateClientProfile(Guid clientProfileId, ClientProfileUpdateParams updateParams)
    {
        throw new NotImplementedException();
    }

    [HttpDelete("client-profiles/{clientProfileId:guid}")]
    public Task DeleteClientProfile(Guid clientProfileId)
    {
        throw new NotImplementedException();
    }

    [HttpPost("settings/open-always-on-page")]
    public void OpenAlwaysOnPage()
    {
        throw new NotImplementedException();
    }

    [HttpPost("settings/request-quick-launch")]
    public Task RequestQuickLaunch()
    {
        throw new NotImplementedException();
    }

    [HttpPost("settings/request-notification")]
    public Task RequestNotification()
    {
        throw new NotImplementedException();
    }
}
