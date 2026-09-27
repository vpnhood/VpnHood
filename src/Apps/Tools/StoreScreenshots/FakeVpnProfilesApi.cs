using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.VpnProfiles;
using VpnHood.AppLib.Api.Premium;

namespace VpnHood.App.StoreScreenshots;

// The profiles, read out of the fixture; nothing about them can be changed from a screenshot.
internal sealed class FakeVpnProfilesApi(FakeAppApi app) : IVpnProfilesApi
{
    public Task<VpnProfileInfo> Get(Guid vpnProfileId, CancellationToken cancellationToken)
    {
        var profile = app.Info.VpnProfileInfos.FirstOrDefault(x => x.VpnProfileId == vpnProfileId)
                      ?? throw new KeyNotFoundException($"The fixture has no profile {vpnProfileId}.");
        return Task.FromResult(profile);
    }

    public Task<VpnProfileInfo> AddByAccessKey(string accessKey, CancellationToken cancellationToken) => throw UnmockedCalls.Record("VpnProfiles.AddByAccessKey");
    public Task<string> GetAccessCode(Guid vpnProfileId, CancellationToken cancellationToken) => throw UnmockedCalls.Record("VpnProfiles.GetAccessCode");
    public Task<VpnProfileInfo> Update(Guid vpnProfileId, VpnProfileUpdateParams updateParams, CancellationToken cancellationToken) => throw UnmockedCalls.Record("VpnProfiles.Update");
    public Task Delete(Guid vpnProfileId, CancellationToken cancellationToken) => throw UnmockedCalls.Record("VpnProfiles.Delete");
    public Task<AppPurchaseOptions> GetPurchaseOptions(Guid vpnProfileId, CancellationToken cancellationToken) => throw UnmockedCalls.Record("VpnProfiles.GetPurchaseOptions");
}
