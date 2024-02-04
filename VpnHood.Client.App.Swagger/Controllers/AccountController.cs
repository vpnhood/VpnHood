using Microsoft.AspNetCore.Mvc;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.WebServer.Api;

namespace VpnHood.Client.App.Swagger.Controllers;

[ApiController]
[Route("api/account")]
public class AccountController : ControllerBase, IAccountController
{
    [HttpGet()]
    public Task<AppAccount> Get()
    {
        throw new NotImplementedException();
    }

    [HttpGet("is-signin-with-google-supported")]
    public bool IsSigninWithGoogleSupported()
    {
        throw new NotImplementedException();
    }

    [HttpPost("signin-with-google")]
    public Task SignInWithGoogle()
    {
        throw new NotImplementedException();
    }

    [HttpPost("sign-out")]
    public new Task SignOut() // TODO Check
    {
        throw new NotImplementedException();
    }

    [HttpGet("is-sign-out")]
    public bool IsSignedOut()
    {
        throw new NotImplementedException();
    }
}