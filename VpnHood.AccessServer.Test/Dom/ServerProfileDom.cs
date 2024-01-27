using VpnHood.AccessServer.Api;

namespace VpnHood.AccessServer.Test.Dom;

public class ServerProfileDom(TestInit testInit, ServerProfile serverProfile)
{
    public TestInit TestInit { get; } = testInit;
    public ServerProfile ServerProfile { get; private set; } = serverProfile;
    public Guid ServerProfileId => ServerProfile.ServerProfileId;
    public ServerProfilesClient Client => TestInit.ServerProfilesClient;

    public static async Task<ServerProfileDom> Create(TestInit? testInit = null, ServerProfileCreateParams? createParams = null)
    {
        testInit ??= await TestInit.Create();
        var serverProfile = await testInit.ServerProfilesClient.CreateAsync(testInit.ProjectId, createParams);
        return new ServerProfileDom(testInit, serverProfile);
    }
    
    public async Task<ServerProfile> Update(ServerProfileUpdateParams updateParams)
    {
        var serverProfile = await Client.UpdateAsync(TestInit.ProjectId, ServerProfileId, updateParams);
        ServerProfile = serverProfile;
        return serverProfile;
    }

    public async Task<ServerProfileData> Reload()
    {
        var data = await Client.GetAsync(TestInit.ProjectId, ServerProfileId, includeSummary: true);
        ServerProfile = data.ServerProfile;
        return data;
    }

    public Task Delete()
    {
        return Client.DeleteAsync(TestInit.ProjectId, ServerProfileId);
    }
}