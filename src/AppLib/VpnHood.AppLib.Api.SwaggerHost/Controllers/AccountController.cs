using Microsoft.AspNetCore.Mvc;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.SwaggerHost.Exceptions;
using SignInResult = VpnHood.AppLib.Contracts.Accounts.SignInResult;
using VpnHood.AppLib.Contracts.Accounts;

namespace VpnHood.AppLib.Api.SwaggerHost.Controllers;

[ApiController]
[Route("api/account")]
public class AccountController : ControllerBase, IAccountApi
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