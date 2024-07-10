using Microsoft.AspNetCore.Mvc;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.Swagger.Exceptions;
using VpnHood.Client.App.WebServer.Api;

namespace VpnHood.Client.App.Swagger.Controllers;

[ApiController]
[Route("api/account")]
public class AccountController : ControllerBase, IAccountController
{
    [HttpGet]
    public Task<AppAccount?> Get()
    {
        throw new SwaggerOnlyException();
    }
    
    [HttpPost("refresh")]
    public Task Refresh()
    {
        throw new SwaggerOnlyException();
    }


    [HttpGet("is-signin-with-google-supported")]
    public bool IsSigninWithGoogleSupported()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("signin-with-google")]
    public Task SignInWithGoogle()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("sign-out")]
    public new Task SignOut()
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("subscriptions/{subscriptionId}/access-keys")]
    public Task<string[]> GetAccessKeys(string subscriptionId)
    {
        throw new SwaggerOnlyException();
    }
}