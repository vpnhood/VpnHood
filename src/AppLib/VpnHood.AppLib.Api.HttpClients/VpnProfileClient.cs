
using VpnHood.AppLib.Api.VpnProfiles;
using VpnHood.AppLib.Api.Premium;

namespace VpnHood.AppLib.Api.HttpClients;

// IVpnProfilesApi over HTTP: the routes of VpnProfileController, one for one.
internal sealed class VpnProfileClient(HttpClient httpClient) : AppApiClientBase(httpClient), IVpnProfilesApi
{
    private const string BaseUrl = "api/vpn-profiles/";

    public Task<VpnProfileInfo> AddByAccessKey(string accessKey, CancellationToken cancellationToken)
    {
        return HttpPutAsync<VpnProfileInfo>(BaseUrl + "access-keys", new Dictionary<string, object?> { ["accessKey"] = accessKey }, null, cancellationToken);
    }

    public Task<VpnProfileInfo> Get(Guid vpnProfileId, CancellationToken cancellationToken)
    {
        return HttpGetAsync<VpnProfileInfo>(BaseUrl + vpnProfileId, null, cancellationToken);
    }

    public Task<string> GetAccessCode(Guid vpnProfileId, CancellationToken cancellationToken)
    {
        return HttpGetAsync<string>(BaseUrl + vpnProfileId + "/access-code", null, cancellationToken);
    }

    public Task<VpnProfileInfo> Update(Guid vpnProfileId, VpnProfileUpdateParams updateParams, CancellationToken cancellationToken)
    {
        return HttpPatchAsync<VpnProfileInfo>(BaseUrl + vpnProfileId, null, updateParams, cancellationToken);
    }

    public Task Delete(Guid vpnProfileId, CancellationToken cancellationToken)
    {
        return HttpDeleteAsync(BaseUrl + vpnProfileId, null, cancellationToken);
    }

    public Task<AppPurchaseOptions> GetPurchaseOptions(Guid vpnProfileId, CancellationToken cancellationToken)
    {
        return HttpGetAsync<AppPurchaseOptions>(BaseUrl + vpnProfileId + "/purchase-options", null, cancellationToken);
    }
}
