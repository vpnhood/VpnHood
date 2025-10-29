using Microsoft.AspNetCore.Mvc;
using VpnHood.AppLib.Swagger.Exceptions;
using VpnHood.AppLib.WebServer.Api;

namespace VpnHood.AppLib.Swagger.Controllers;

[ApiController]
[Route("api/intents")]
public class IntentsController : ControllerBase, IIntentController
{
    [HttpPost("request-quick-launch")]
    public Task<bool> RequestQuickLaunch()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("request-user-review")]
    public Task RequestUserReview()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("request-notification")]
    public Task<bool> RequestNotification()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("open-kill-switch-settings")]
    public Task OpenKillSwitchSettings()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("open-always-on-settings")]
    public Task OpenAlwaysOnSettings()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("open-settings")]
    public Task OpenSettings()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("open-app-settings")]
    public Task OpenAppSettings()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("open-app-notification-settings")]
    public Task OpenAppNotificationSettings()
    {
        throw new SwaggerOnlyException();
    }
}