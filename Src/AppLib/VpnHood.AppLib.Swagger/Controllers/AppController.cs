using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AppLib.Settings;
using VpnHood.AppLib.Swagger.Exceptions;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.AppLib.Swagger.Controllers;

[ApiController]
[Route("api/app")]
public class AppController : ControllerBase, IAppController
{
    [HttpPatch("configure")]
    public Task<AppData> Configure(ConfigParams configParams)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("config")]
    public Task<AppData> GetConfig()
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("ip-filters")]
    public Task<IpFilters> GetIpFilters()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPut("ip-filters")]
    public Task SetIpFilters(IpFilters ipFilters)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("state")]
    public Task<AppState> GetState()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("connect")]
    public Task Connect(Guid? clientProfileId = null, string? serverLocation = null,
        ConnectPlanId planId = ConnectPlanId.Normal)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("diagnose")]
    public Task Diagnose(Guid? clientProfileId = null, string? serverLocation = null,
        ConnectPlanId planId = ConnectPlanId.Normal)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("disconnect")]
    public Task Disconnect()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("clear-last-error")]
    public Task ClearLastError()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("extend-by-rewarded-ad")]
    public Task ExtendByRewardedAd()
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

    [HttpPost("version-check")]
    public Task VersionCheck()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("version-check-postpone")]
    public Task VersionCheckPostpone()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("settings/open-always-on-page")]
    public Task OpenAlwaysOnPage()
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

    [HttpGet("exception-types")]
    public Task<ExceptionType[]> GetExceptionTypes()
    {
        throw new SwaggerOnlyException();
    }
}