using VpnHood.AccessServer.Api;

namespace VpnHood.AccessServer.Test.Dom;

public class ServerProfileDom(TestApp testApp, ServerProfile serverProfile)
{
    public TestApp TestApp { get; } = testApp;
    public ServerProfile ServerProfile { get; private set; } = serverProfile;
    public Guid ServerProfileId => ServerProfile.ServerProfileId;
    public ServerProfilesClient Client => TestApp.ServerProfilesClient;

    public static async Task<ServerProfileDom> Create(TestApp? testApp = null,
        ServerProfileCreateParams? createParams = null)
    {
        testApp ??= await TestApp.Create();
        var serverProfile = await testApp.ServerProfilesClient.CreateAsync(testApp.ProjectId, createParams);
        return new ServerProfileDom(testApp, serverProfile);
    }

    public async Task<ServerProfile> Update(ServerProfileUpdateParams updateParams)
    {
        var serverProfile = await Client.UpdateAsync(TestApp.ProjectId, ServerProfileId, updateParams);
        ServerProfile = serverProfile;
        return serverProfile;
    }

    public async Task<ServerProfileData> Reload()
    {
        var data = await Client.GetAsync(TestApp.ProjectId, ServerProfileId, includeSummary: true);
        ServerProfile = data.ServerProfile;
        return data;
    }

    public Task Delete()
    {
        return Client.DeleteAsync(TestApp.ProjectId, ServerProfileId);
    }
}