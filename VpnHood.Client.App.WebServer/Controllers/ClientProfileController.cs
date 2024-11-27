using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VpnHood.Client.App.ClientProfiles;
using VpnHood.Client.App.WebServer.Api;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.WebServer.Controllers;

internal class ClientProfileController : WebApiController, IClientProfileController
{
    private static VpnHoodApp App => VpnHoodApp.Instance;
    
    [Route(HttpVerbs.Put, "/access-keys")]
    public Task<ClientProfileInfo> AddByAccessKey([QueryField] string accessKey)
    {
        var clientProfile = App.ClientProfileService.ImportAccessKey(accessKey);
        return Task.FromResult(clientProfile.ToInfo());
    }

    [Route(HttpVerbs.Get, "/{clientProfileId}")]
    public Task<ClientProfileInfo> Get(Guid clientProfileId)
    {
        var clientProfile = App.ClientProfileService.Get(clientProfileId);
        return Task.FromResult(clientProfile.ToInfo());
    }

    [Route(HttpVerbs.Patch, "/{clientProfileId}")]
    public async Task<ClientProfileInfo> Update(Guid clientProfileId, ClientProfileUpdateParams updateParams)
    {
        _ = updateParams;
        updateParams = await HttpContext.GetRequestDataAsync<ClientProfileUpdateParams>().VhConfigureAwait();
        var clientProfile = App.ClientProfileService.Update(clientProfileId, updateParams);
        return clientProfile.ToInfo();
    }

    [Route(HttpVerbs.Delete, "/{clientProfileId}")]
    public async Task Delete(Guid clientProfileId)
    {
        if (clientProfileId == App.CurrentClientProfileInfo?.ClientProfileId)
            await App.Disconnect(true).VhConfigureAwait();

        App.ClientProfileService.Remove(clientProfileId);
    }
}