using Microsoft.AspNetCore.Mvc;
using VpnHood.Client.App.ClientProfiles;
using VpnHood.Client.App.Swagger.Exceptions;
using VpnHood.Client.App.WebServer.Api;

namespace VpnHood.Client.App.Swagger.Controllers;

[ApiController]
[Route("api/client-profiles")]
public class ClientProfileController : ControllerBase, IClientProfileController
{

    [HttpPut("access-keys")]
    public Task<ClientProfileInfo> AddByAccessKey(string accessKey)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("{clientProfileId:guid}")]
    public Task<ClientProfileInfo> Get(Guid clientProfileId)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPatch("{clientProfileId:guid}")]
    public Task<ClientProfileInfo> Update(Guid clientProfileId, ClientProfileUpdateParams updateParams)
    {
        throw new SwaggerOnlyException();
    }

    [HttpDelete("{clientProfileId:guid}")]
    public Task Delete(Guid clientProfileId)
    {
        throw new SwaggerOnlyException();
    }
}