using Microsoft.AspNetCore.Mvc;
using VpnHood.AppLib.Swagger.Exceptions;
using VpnHood.AppLib.WebServer.Api;

namespace VpnHood.AppLib.Swagger.Controllers;

[ApiController]
[Route("api/intents")]
public class IntentsController : ControllerBase, IIntentController
{
    [HttpGet("status")]
    public Task<IntentStatus> GetStatus()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("request-quick-launch")]
    public Task RequestQuickLaunch()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("request-user-review")]
    public Task RequestUserReview()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("request-notification")]
    public Task RequestNotification()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("open-system-kill-switch-settings")]
    public Task OpenSystemKillSwitchSettings()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("open-system-always-on-settings")]
    public Task OpenSystemAlwaysOnSettings()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("open-system-settings")]
    public Task OpenSystemSettings()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("open-app-system-settings")]
    public Task OpenAppSystemSettings()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("open-app-system-notification-settings")]
    public Task OpenAppSystemNotificationSettings()
    {
        throw new SwaggerOnlyException();
    }
}