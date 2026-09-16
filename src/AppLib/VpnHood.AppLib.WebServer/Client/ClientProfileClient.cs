using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.WebServer.Api;

namespace VpnHood.AppLib.WebServer.Client;

// IClientProfileController over HTTP: the routes of ClientProfileController, one for one.
internal sealed class ClientProfileClient(HttpClient httpClient) : AppApiClientBase(httpClient), IClientProfileController
{
    private const string BaseUrl = "api/client-profiles/";

    public Task<ClientProfileInfo> AddByAccessKey(string accessKey, CancellationToken cancellationToken)
    {
        return PutAsync<ClientProfileInfo>(BaseUrl + "access-keys",
            new Dictionary<string, object?> { ["accessKey"] = accessKey }, cancellationToken);
    }

    public Task<ClientProfileInfo> Get(Guid clientProfileId, CancellationToken cancellationToken)
    {
        return GetAsync<ClientProfileInfo>(BaseUrl + clientProfileId, null, cancellationToken);
    }

    public Task<string> GetAccessCode(Guid clientProfileId, CancellationToken cancellationToken)
    {
        return GetAsync<string>(BaseUrl + clientProfileId + "/access-code", null, cancellationToken);
    }

    public Task<ClientProfileInfo> Update(Guid clientProfileId, ClientProfileUpdateParams updateParams, CancellationToken cancellationToken)
    {
        return PatchAsync<ClientProfileUpdateParams, ClientProfileInfo>(BaseUrl + clientProfileId, updateParams, cancellationToken);
    }

    public Task Delete(Guid clientProfileId, CancellationToken cancellationToken)
    {
        return DeleteAsync(BaseUrl + clientProfileId, null, cancellationToken);
    }

    public Task<AppPurchaseOptions> GetPurchaseOptions(Guid clientProfileId, CancellationToken cancellationToken)
    {
        return GetAsync<AppPurchaseOptions>(BaseUrl + clientProfileId + "/purchase-options", null, cancellationToken);
    }
}
