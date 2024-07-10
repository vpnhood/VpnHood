using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using VpnHood.Client.App.ClientProfiles;
using VpnHood.Client.App.Settings;
using VpnHood.Client.App.Swagger.Exceptions;
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
        throw new SwaggerOnlyException();
    }

    [HttpGet("config")]
    public Task<AppConfig> GetConfig()
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("state")]
    public Task<AppState> GetState()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("connect")]
    public Task Connect(Guid? clientProfileId = null, string? serverLocation = null)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("diagnose")]
    public Task Diagnose(Guid? clientProfileId = null, string? serverLocation = null)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("disconnect")]
    public Task Disconnect()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("clear-last-error")]
    public void ClearLastError()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPut("user-settings")]
    public Task SetUserSettings(UserSettings userSettings)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("log.txt")]
    [Produces(MediaTypeNames.Text.Plain)]
    public Task<string> Log()
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("installed-apps")]
    public Task<DeviceAppInfo[]> GetInstalledApps()
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("ip-groups")]
    public Task<IpGroupInfo[]> GetIpGroups()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("version-check")]
    public Task VersionCheck()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("version-check-postpone")]
    public void VersionCheckPostpone()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPut("access-keys")]
    public Task<ClientProfileInfo> AddAccessKey(string accessKey)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPatch("client-profiles/{clientProfileId:guid}")]
    public Task<ClientProfileInfo> UpdateClientProfile(Guid clientProfileId, ClientProfileUpdateParams updateParams)
    {
        throw new SwaggerOnlyException();
    }

    [HttpDelete("client-profiles/{clientProfileId:guid}")]
    public Task DeleteClientProfile(Guid clientProfileId)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("settings/open-always-on-page")]
    public void OpenAlwaysOnPage()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("settings/request-quick-launch")]
    public Task RequestQuickLaunch()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("settings/request-notification")]
    public Task RequestNotification()
    {
        throw new SwaggerOnlyException();
    }
}
