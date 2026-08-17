using Microsoft.AspNetCore.Mvc;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Swagger.Exceptions;
using VpnHood.AppLib.WebServer.Api;
using SignInResult = VpnHood.AppLib.Abstractions.Accounts.SignInResult;

namespace VpnHood.AppLib.Swagger.Controllers;

[ApiController]
[Route("api/account")]
public class AccountController : ControllerBase, IAccountController
{
    [HttpGet]
    public Task<Account?> Get(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("refresh")]
    public Task Refresh(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }


    [HttpPost("sign-in")]
    public Task<SignInResult> SignIn(SignInOptions signInOptions, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("sign-out")]
    public Task SignOut(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpDelete]
    public Task Delete(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

}