using Microsoft.AspNetCore.Mvc;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.SwaggerHost.Exceptions;
using VpnHood.AppLib.Contracts.ClientProfiles;
using VpnHood.AppLib.Contracts.Premium;

namespace VpnHood.AppLib.Api.SwaggerHost.Controllers;

[ApiController]
[Route("api/client-profiles")]
public class ClientProfileController : ControllerBase, IClientProfilesApi
{
    [HttpPut("access-keys")]
    public Task<ClientProfileInfo> AddByAccessKey(string accessKey, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("{clientProfileId}")]
    public Task<ClientProfileInfo> Get(Guid clientProfileId, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("{clientProfileId}/access-code")]
    public Task<string> GetAccessCode(Guid clientProfileId, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPatch("{clientProfileId}")]
    public Task<ClientProfileInfo> Update(Guid clientProfileId, ClientProfileUpdateParams updateParams,
        CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpDelete("{clientProfileId}")]
    public Task Delete(Guid clientProfileId, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("{clientProfileId}/purchase-options")]
    public Task<AppPurchaseOptions> GetPurchaseOptions(Guid clientProfileId, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }
}