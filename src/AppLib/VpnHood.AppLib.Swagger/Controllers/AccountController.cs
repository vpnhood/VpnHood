using Microsoft.AspNetCore.Mvc;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Swagger.Exceptions;
using VpnHood.AppLib.WebServer.Api;

namespace VpnHood.AppLib.Swagger.Controllers;

[ApiController]
[Route("api/account")]
public class AccountController : ControllerBase, IAccountController
{
    [HttpGet]
    public Task<AppAccount?> Get(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("refresh")]
    public Task Refresh(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }


    [HttpPost("sign-in")]
    public Task SignIn(AppSignInOptions signInOptions, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("sign-out")]
    public Task SignOut(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("subscriptions/{subscriptionId}/access-keys")]
    public Task<IReadOnlyList<string>> ListAccessKeys(string subscriptionId, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }
}