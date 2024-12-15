using VpnHood.AppLib.ClientProfiles;

namespace VpnHood.AppLib.WebServer.Api;

public interface IClientProfileController
{
    Task<ClientProfileInfo> AddByAccessKey(string accessKey);
    Task<ClientProfileInfo> Get(Guid clientProfileId);
    Task<ClientProfileInfo> Update(Guid clientProfileId, ClientProfileUpdateParams updateParams);
    Task Delete(Guid clientProfileId);
}