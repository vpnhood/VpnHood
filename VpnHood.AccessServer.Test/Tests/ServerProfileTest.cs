using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.Common.Client;
using VpnHood.Common.Exceptions;
using VpnHood.Server.Configurations;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ServerProfileTest
{
    [TestMethod]
    public async Task Crud()
    {
        var testInit = await TestInit.Create();
        // ReSharper disable once UseObjectOrCollectionInitializer

        // -----------
        // Create
        // -----------
        var serverConfig = new ServerConfig();
        serverConfig.SessionOptions.NetScanLimit = 1000;
        var serverProfile = await testInit.ServerProfilesClient.CreateAsync(testInit.ProjectId, new Api.ServerProfileCreateParams
        {
            ServerConfig = JsonSerializer.Serialize(serverConfig)
        });
        serverConfig = serverProfile.ServerConfig != null ? JsonSerializer.Deserialize<ServerConfig>(serverProfile.ServerConfig) : null;
        Assert.AreEqual(1000, serverConfig?.SessionOptions.NetScanLimit);

        // -----------
        // get
        // -----------
        serverProfile = await testInit.ServerProfilesClient.GetAsync(testInit.ProjectId, serverProfile.ServerProfileId);
        serverConfig = JsonSerializer.Deserialize<ServerConfig>(serverProfile.ServerConfig!);
        Assert.AreEqual(1000, serverConfig!.SessionOptions.NetScanLimit);

        // -----------
        // update
        // -----------
        serverConfig.SessionOptions.NetScanLimit = 2000;
        await testInit.ServerProfilesClient.UpdateAsync(testInit.ProjectId, serverProfile.ServerProfileId, new ServerProfileUpdateParams
        {
            ServerConfig = new PatchOfString { Value = JsonSerializer.Serialize(serverConfig) }
        });
        Assert.AreEqual(2000, serverConfig.SessionOptions.NetScanLimit);
        serverProfile = await testInit.ServerProfilesClient.GetAsync(testInit.ProjectId, serverProfile.ServerProfileId);
        serverConfig = JsonSerializer.Deserialize<ServerConfig>(serverProfile.ServerConfig!);
        Assert.AreEqual(2000, serverConfig!.SessionOptions.NetScanLimit);

        // -----------
        // Delete
        // -----------
        await testInit.ServerProfilesClient.DeleteAsync(testInit.ProjectId, serverProfile.ServerProfileId);
        try
        {
            await testInit.ServerProfilesClient.GetAsync(testInit.ProjectId, serverProfile.ServerProfileId);
            Assert.Fail("NotExistsException was expected");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }
}