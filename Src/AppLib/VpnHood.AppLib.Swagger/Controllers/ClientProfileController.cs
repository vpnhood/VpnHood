using Microsoft.AspNetCore.Mvc;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Swagger.Exceptions;
using VpnHood.AppLib.WebServer.Api;

namespace VpnHood.AppLib.Swagger.Controllers;

[ApiController]
[Route("api/client-profiles")]
public class ClientProfileController : ControllerBase, IClientProfileController
{
    [HttpPut("access-keys")]
    public Task<ClientProfileInfo> AddByAccessKey(string accessKey)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("{clientProfileId}")]
    public Task<ClientProfileInfo> Get(Guid clientProfileId)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPatch("{clientProfileId}")]
    public Task<ClientProfileInfo> Update(Guid clientProfileId, ClientProfileUpdateParams updateParams)
    {
        throw new SwaggerOnlyException();
    }

    [HttpDelete("{clientProfileId}")]
    public Task Delete(Guid clientProfileId)
    {
        throw new SwaggerOnlyException();
    }
}