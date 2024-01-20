using Microsoft.AspNetCore.Mvc;
using VpnHood.Client.App.Accounts;
using VpnHood.Client.App.WebServer.Api;

namespace VpnHood.Client.App.Swagger.Controllers;

[ApiController]
[Route("api/account")]
public class AccountController : ControllerBase, IAccountController
{
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

    [HttpGet("account")]
    public Task<Account> GetAccount()
    {
        throw new NotImplementedException();
    }
}