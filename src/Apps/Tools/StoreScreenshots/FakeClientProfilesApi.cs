using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.ClientProfiles;
using VpnHood.AppLib.Api.Premium;

namespace VpnHood.App.StoreScreenshots;

// The profiles, read out of the fixture; nothing about them can be changed from a screenshot.
internal sealed class FakeClientProfilesApi(FakeAppApi app) : IClientProfilesApi
{
    public Task<ClientProfileInfo> Get(Guid clientProfileId, CancellationToken cancellationToken)
    {
        var profile = app.Info.ClientProfileInfos.FirstOrDefault(x => x.ClientProfileId == clientProfileId)
                      ?? throw new KeyNotFoundException($"The fixture has no profile {clientProfileId}.");
        return Task.FromResult(profile);
    }

    public Task<ClientProfileInfo> AddByAccessKey(string accessKey, CancellationToken cancellationToken) => throw UnmockedCalls.Record("ClientProfiles.AddByAccessKey");
    public Task<string> GetAccessCode(Guid clientProfileId, CancellationToken cancellationToken) => throw UnmockedCalls.Record("ClientProfiles.GetAccessCode");
    public Task<ClientProfileInfo> Update(Guid clientProfileId, ClientProfileUpdateParams updateParams, CancellationToken cancellationToken) => throw UnmockedCalls.Record("ClientProfiles.Update");
    public Task Delete(Guid clientProfileId, CancellationToken cancellationToken) => throw UnmockedCalls.Record("ClientProfiles.Delete");
    public Task<AppPurchaseOptions> GetPurchaseOptions(Guid clientProfileId, CancellationToken cancellationToken) => throw UnmockedCalls.Record("ClientProfiles.GetPurchaseOptions");
}
