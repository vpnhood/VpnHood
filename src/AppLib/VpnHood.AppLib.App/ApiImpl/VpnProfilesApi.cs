using VpnHood.AppLib.App.VpnProfiles;
using VpnHood.AppLib.Api.VpnProfiles;
using VpnHood.AppLib.Api.Premium;
using VpnHood.AppLib.Api;

namespace VpnHood.AppLib.App.ApiImpl;

internal sealed class VpnProfilesApi(VpnHoodApp app) : IVpnProfilesApi
{
    public Task<VpnProfileInfo> AddByAccessKey(string accessKey, CancellationToken cancellationToken)
    {
        var vpnProfile = app.VpnProfileService.ImportAccessKey(accessKey);
        return Task.FromResult(vpnProfile.ToInfo(app.Features));
    }

    public Task<VpnProfileInfo> Get(Guid vpnProfileId, CancellationToken cancellationToken)
    {
        var vpnProfile = app.VpnProfileService.Get(vpnProfileId);
        return Task.FromResult(vpnProfile.ToInfo(app.Features));
    }

    public Task<string> GetAccessCode(Guid vpnProfileId, CancellationToken cancellationToken)
    {
        var vpnProfile = app.VpnProfileService.Get(vpnProfileId);
        return Task.FromResult(vpnProfile.AccessCode ?? string.Empty);
    }

    public Task<VpnProfileInfo> Update(Guid vpnProfileId, VpnProfileUpdateParams updateParams,
        CancellationToken cancellationToken)
    {
        return app.UpdateVpnProfile(vpnProfileId, updateParams, cancellationToken);
    }

    public async Task Delete(Guid vpnProfileId, CancellationToken cancellationToken)
    {
        if (!app.IsIdle && vpnProfileId == app.CurrentVpnProfileInfo?.VpnProfileId)
            await app.Disconnect();

        app.VpnProfileService.Delete(vpnProfileId);
    }

    public Task<AppPurchaseOptions> GetPurchaseOptions(Guid vpnProfileId, CancellationToken cancellationToken)
    {
        return app.GetPurchaseOptions(vpnProfileId, cancellationToken);
    }
}