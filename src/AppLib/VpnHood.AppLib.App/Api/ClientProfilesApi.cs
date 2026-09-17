using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Api.ClientProfiles;
using VpnHood.AppLib.Api.Premium;

namespace VpnHood.AppLib.Api;

internal sealed class ClientProfilesApi(VpnHoodApp app) : IClientProfilesApi
{
    public Task<ClientProfileInfo> AddByAccessKey(string accessKey, CancellationToken cancellationToken)
    {
        var clientProfile = app.ClientProfileService.ImportAccessKey(accessKey);
        return Task.FromResult(clientProfile.ToInfo(app.Features));
    }

    public Task<ClientProfileInfo> Get(Guid clientProfileId, CancellationToken cancellationToken)
    {
        var clientProfile = app.ClientProfileService.Get(clientProfileId);
        return Task.FromResult(clientProfile.ToInfo(app.Features));
    }

    public Task<string> GetAccessCode(Guid clientProfileId, CancellationToken cancellationToken)
    {
        var clientProfile = app.ClientProfileService.Get(clientProfileId);
        return Task.FromResult(clientProfile.AccessCode ?? string.Empty);
    }

    public Task<ClientProfileInfo> Update(Guid clientProfileId, ClientProfileUpdateParams updateParams,
        CancellationToken cancellationToken)
    {
        return app.UpdateClientProfile(clientProfileId, updateParams, cancellationToken);
    }

    public async Task Delete(Guid clientProfileId, CancellationToken cancellationToken)
    {
        if (!app.IsIdle && clientProfileId == app.CurrentClientProfileInfo?.ClientProfileId)
            await app.Disconnect();

        app.ClientProfileService.Delete(clientProfileId);
    }

    public Task<AppPurchaseOptions> GetPurchaseOptions(Guid clientProfileId, CancellationToken cancellationToken)
    {
        return app.GetPurchaseOptions(clientProfileId, cancellationToken);
    }
}