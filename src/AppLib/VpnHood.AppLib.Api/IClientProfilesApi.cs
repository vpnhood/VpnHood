

using VpnHood.AppLib.Api.ClientProfiles;
using VpnHood.AppLib.Api.Premium;

namespace VpnHood.AppLib.Api;

public interface IClientProfilesApi
{
    Task<ClientProfileInfo> AddByAccessKey(string accessKey, CancellationToken cancellationToken);
    Task<ClientProfileInfo> Get(Guid clientProfileId, CancellationToken cancellationToken);
    Task<string> GetAccessCode(Guid clientProfileId, CancellationToken cancellationToken);
    Task<ClientProfileInfo> Update(Guid clientProfileId, ClientProfileUpdateParams updateParams, CancellationToken cancellationToken);
    Task Delete(Guid clientProfileId, CancellationToken cancellationToken);
    Task<AppPurchaseOptions> GetPurchaseOptions(Guid clientProfileId, CancellationToken cancellationToken);
}