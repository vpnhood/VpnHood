using Microsoft.AspNetCore.Mvc;
using VpnHood.AppLib.Swagger.Exceptions;
using VpnHood.AppLib.WebServer.Api;

namespace VpnHood.AppLib.Swagger.Controllers;

[ApiController]
[Route("api/intents")]
public class IntentsController : ControllerBase, IIntentController
{
    [HttpPost("request-quick-launch")]
    public Task<bool> RequestQuickLaunch(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("request-user-review")]
    public Task RequestUserReview(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("request-notification")]
    public Task<bool> RequestNotification(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("open-kill-switch-settings")]
    public Task OpenKillSwitchSettings()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("open-always-on-settings")]
    public Task OpenAlwaysOnSettings(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("open-settings")]
    public Task OpenSettings(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("open-app-settings")]
    public Task OpenAppSettings(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("open-app-notification-settings")]
    public Task OpenAppNotificationSettings(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }
}