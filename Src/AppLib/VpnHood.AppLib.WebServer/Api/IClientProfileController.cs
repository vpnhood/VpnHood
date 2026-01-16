using VpnHood.AppLib.ClientProfiles;

namespace VpnHood.AppLib.WebServer.Api;

public interface IClientProfileController
{
    Task<ClientProfileInfo> AddByAccessKey(string accessKey, CancellationToken cancellationToken);
    Task<ClientProfileInfo> Get(Guid clientProfileId, CancellationToken cancellationToken);
    Task<ClientProfileInfo> Update(Guid clientProfileId, ClientProfileUpdateParams updateParams, CancellationToken cancellationToken);
    Task Delete(Guid clientProfileId, CancellationToken cancellationToken);
}