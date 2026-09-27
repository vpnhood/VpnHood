

using VpnHood.AppLib.Api.VpnProfiles;
using VpnHood.AppLib.Api.Premium;

namespace VpnHood.AppLib.Api;

public interface IVpnProfilesApi
{
    Task<VpnProfileInfo> AddByAccessKey(string accessKey, CancellationToken cancellationToken);
    Task<VpnProfileInfo> Get(Guid vpnProfileId, CancellationToken cancellationToken);
    Task<string> GetAccessCode(Guid vpnProfileId, CancellationToken cancellationToken);
    Task<VpnProfileInfo> Update(Guid vpnProfileId, VpnProfileUpdateParams updateParams, CancellationToken cancellationToken);
    Task Delete(Guid vpnProfileId, CancellationToken cancellationToken);
    Task<AppPurchaseOptions> GetPurchaseOptions(Guid vpnProfileId, CancellationToken cancellationToken);
}