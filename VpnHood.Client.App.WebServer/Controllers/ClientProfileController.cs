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
    public Task<ClientProfileInfo> AddByAccessKey(string accessKey)
    {
        var clientProfile = App.ClientProfileService.ImportAccessKey(accessKey);
        return Task.FromResult(clientProfile.ClientProfileInfo);
    }

    [Route(HttpVerbs.Get, "/{clientProfileId}")]
    public Task<ClientProfileInfo> Get(Guid clientProfileId)
    {
        var clientProfileItem = App.ClientProfileService.Get(clientProfileId);
        return Task.FromResult(clientProfileItem.ClientProfileInfo);
    }

    [Route(HttpVerbs.Patch, "/{clientProfileId}")]
    public async Task<ClientProfileInfo> Update(Guid clientProfileId, ClientProfileUpdateParams updateParams)
    {
        _ = updateParams;
        updateParams = await HttpContext.GetRequestDataAsync<ClientProfileUpdateParams>().VhConfigureAwait();
        var clientProfileItem = App.ClientProfileService.Update(clientProfileId, updateParams);
        return clientProfileItem.ClientProfileInfo;
    }

    [Route(HttpVerbs.Delete, "/{clientProfileId}")]
    public async Task Delete(Guid clientProfileId)
    {
        if (clientProfileId == App.CurrentClientProfileItem?.ClientProfileId)
            await App.Disconnect(true).VhConfigureAwait();

        App.ClientProfileService.Remove(clientProfileId);
    }
}