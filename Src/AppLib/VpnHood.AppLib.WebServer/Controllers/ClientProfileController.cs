using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.WebServer.Api;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class ClientProfileController : IClientProfileController
{
    private static VpnHoodApp App => VpnHoodApp.Instance;

    public Task<ClientProfileInfo> AddByAccessKey(string accessKey)
    {
        var clientProfile = App.ClientProfileService.ImportAccessKey(accessKey);
        return Task.FromResult(clientProfile.ToInfo());
    }

    public Task<ClientProfileInfo> Get(Guid clientProfileId)
    {
        var clientProfile = App.ClientProfileService.Get(clientProfileId);
        return Task.FromResult(clientProfile.ToInfo());
    }

    public Task<ClientProfileInfo> Update(Guid clientProfileId, ClientProfileUpdateParams updateParams)
    {
        var clientProfile = App.ClientProfileService.Update(clientProfileId, updateParams);
        return Task.FromResult(clientProfile.ToInfo());
    }

    public Task Delete(Guid clientProfileId)
    {
        if (clientProfileId == App.CurrentClientProfileInfo?.ClientProfileId)
            return App.Disconnect();

        App.ClientProfileService.Delete(clientProfileId);
        return Task.CompletedTask;
    }
}