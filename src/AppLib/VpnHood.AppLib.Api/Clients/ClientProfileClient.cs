
using VpnHood.AppLib.Contracts.ClientProfiles;
using VpnHood.AppLib.Contracts.Premium;

namespace VpnHood.AppLib.Api.Clients;

// IClientProfilesApi over HTTP: the routes of ClientProfileController, one for one.
internal sealed class ClientProfileClient(HttpClient httpClient) : AppApiClientBase(httpClient), IClientProfilesApi
{
    private const string BaseUrl = "api/client-profiles/";

    public Task<ClientProfileInfo> AddByAccessKey(string accessKey, CancellationToken cancellationToken)
    {
        return HttpPutAsync<ClientProfileInfo>(BaseUrl + "access-keys", new Dictionary<string, object?> { ["accessKey"] = accessKey }, null, cancellationToken);
    }

    public Task<ClientProfileInfo> Get(Guid clientProfileId, CancellationToken cancellationToken)
    {
        return HttpGetAsync<ClientProfileInfo>(BaseUrl + clientProfileId, null, cancellationToken);
    }

    public Task<string> GetAccessCode(Guid clientProfileId, CancellationToken cancellationToken)
    {
        return HttpGetAsync<string>(BaseUrl + clientProfileId + "/access-code", null, cancellationToken);
    }

    public Task<ClientProfileInfo> Update(Guid clientProfileId, ClientProfileUpdateParams updateParams, CancellationToken cancellationToken)
    {
        return HttpPatchAsync<ClientProfileInfo>(BaseUrl + clientProfileId, null, updateParams, cancellationToken);
    }

    public Task Delete(Guid clientProfileId, CancellationToken cancellationToken)
    {
        return HttpDeleteAsync(BaseUrl + clientProfileId, null, cancellationToken);
    }

    public Task<AppPurchaseOptions> GetPurchaseOptions(Guid clientProfileId, CancellationToken cancellationToken)
    {
        return HttpGetAsync<AppPurchaseOptions>(BaseUrl + clientProfileId + "/purchase-options", null, cancellationToken);
    }
}
