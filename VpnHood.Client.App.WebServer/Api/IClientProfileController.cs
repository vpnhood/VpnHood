using VpnHood.Client.App.ClientProfiles;

namespace VpnHood.Client.App.WebServer.Api;

public interface IClientProfileController
{
    Task<ClientProfileInfo> AddByAccessKey(string accessKey);
    Task<ClientProfileInfo> Get(Guid clientProfileId);
    Task<ClientProfileInfo> Update(Guid clientProfileId, ClientProfileUpdateParams updateParams);
    Task Delete(Guid clientProfileId);
}