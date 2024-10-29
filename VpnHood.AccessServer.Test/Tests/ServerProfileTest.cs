using System.Text.Json;
using GrayMint.Common.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.ApiClients;
using VpnHood.Common.Exceptions;
using VpnHood.Server.Access.Configurations;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ServerProfileTest
{
    [TestMethod]
    public async Task Default_ServerProfile()
    {
        using var testApp = await TestApp.Create();

        // -----------
        // Make sure default is created
        // -----------
        var serverProfiles = await testApp.ServerProfilesClient.ListAsync(testApp.ProjectId);
        var defaultServerProfile = serverProfiles.Single(x => x.ServerProfile.IsDefault).ServerProfile;
        Assert.IsNotNull(defaultServerProfile);

        var serverProfileDom = new ServerProfileDom(testApp, defaultServerProfile);

        // -----------
        // Default can not be deleted
        // -----------
        try {
            await serverProfileDom.Delete();
            Assert.Fail("Default ServerProfile should not be deletable.");
        }
        catch (ApiException ex) {
            Assert.AreEqual(ex.ExceptionTypeName, nameof(InvalidOperationException));
        }

        // -----------
        // Default can not be renamed
        // -----------
        try {
            await serverProfileDom.Update(new ServerProfileUpdateParams {
                ServerProfileName = new PatchOfString { Value = Guid.NewGuid().ToString() }
            });
            Assert.Fail("Default ServerProfile should not be deletable.");
        }
        catch (ApiException ex) {
            Assert.AreEqual(ex.ExceptionTypeName, nameof(InvalidOperationException));
        }
    }

    [TestMethod]
    public async Task Crud()
    {
        using var testApp = await TestApp.Create();

        // -----------
        // Create
        // -----------
        // ReSharper disable once UseObjectOrCollectionInitializer
        var serverConfig = new ServerConfig();
        serverConfig.SessionOptions.NetScanLimit = 1000;
        var serverProfileDom = await ServerProfileDom.Create(testApp, new ServerProfileCreateParams {
            ServerConfig = JsonSerializer.Serialize(serverConfig)
        });
        Assert.IsNotNull(serverProfileDom.ServerProfile.ServerConfig);
        var serverConfig2 = GmUtil.JsonDeserialize<ServerConfig>(serverProfileDom.ServerProfile.ServerConfig);
        Assert.AreEqual(serverConfig.SessionOptions.NetScanLimit, serverConfig2.SessionOptions.NetScanLimit);

        // -----------
        // get
        // -----------
        await serverProfileDom.Reload();
        serverConfig2 = GmUtil.JsonDeserialize<ServerConfig>(serverProfileDom.ServerProfile.ServerConfig);
        Assert.AreEqual(serverConfig.SessionOptions.NetScanLimit, serverConfig2.SessionOptions.NetScanLimit);

        // -----------
        // update
        // -----------
        serverConfig.SessionOptions.NetScanLimit = 2000;
        serverConfig.SwapMemorySizeMb = 500;
        await serverProfileDom.Update(new ServerProfileUpdateParams {
            ServerConfig = new PatchOfString { Value = JsonSerializer.Serialize(serverConfig) }
        });
        serverConfig2 = GmUtil.JsonDeserialize<ServerConfig>(serverProfileDom.ServerProfile.ServerConfig);
        Assert.AreEqual(serverConfig.SessionOptions.NetScanLimit, serverConfig2.SessionOptions.NetScanLimit);
        Assert.AreEqual(serverConfig.SwapMemorySizeMb, serverConfig2.SwapMemorySizeMb);

        // -----------
        // Delete
        // -----------
        await serverProfileDom.Delete();
        try {
            await serverProfileDom.Reload();
            Assert.Fail("NotExistsException was expected");
        }
        catch (ApiException ex) {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Reconfigure_all_servers_on_update()
    {
        using var testApp = await TestApp.Create();
        var serverProfileDom = await ServerProfileDom.Create(testApp);

        // farm1
        using var farm1 = await ServerFarmDom.Create(testApp, new ServerFarmCreateParams {
            ServerProfileId = serverProfileDom.ServerProfileId
        });
        var serverDom1 = await farm1.AddNewServer();
        var serverDom2 = await farm1.AddNewServer();

        // farm2
        using var farm2 = await ServerFarmDom.Create(testApp, new ServerFarmCreateParams {
            ServerProfileId = serverProfileDom.ServerProfileId
        });
        var serverDom3 = await farm2.AddNewServer();
        var serverDom4 = await farm2.AddNewServer();

        // update ServerProfile
        var config = new ServerConfig { SessionOptions = new SessionOptions { TcpBufferSize = 0x2000 } };
        await serverProfileDom.Update(new ServerProfileUpdateParams {
            ServerConfig = new PatchOfString { Value = JsonSerializer.Serialize(config) }
        });

        // check serverConfig
        Assert.AreNotEqual(serverDom1.ServerStatus.ConfigCode, (await serverDom1.SendStatus()).ConfigCode);
        Assert.AreNotEqual(serverDom2.ServerStatus.ConfigCode, (await serverDom2.SendStatus()).ConfigCode);
        Assert.AreNotEqual(serverDom3.ServerStatus.ConfigCode, (await serverDom3.SendStatus()).ConfigCode);
        Assert.AreNotEqual(serverDom4.ServerStatus.ConfigCode, (await serverDom4.SendStatus()).ConfigCode);

        // reconfig
        await serverDom1.Configure();
        Assert.AreEqual(config.SessionOptions.TcpBufferSize, serverDom1.ServerConfig.SessionOptions.TcpBufferSize);
        Assert.AreEqual(true, serverDom1.ServerConfig.TrackingOptions.TrackTcp, "TrackTcp must be set by default.");
        Assert.AreEqual(true, serverDom1.ServerConfig.TrackingOptions.TrackLocalPort,
            "TrackTcp must be set by default.");
    }

    [TestMethod]
    public async Task Get_with_summaries()
    {
        using var testApp = await TestApp.Create();
        var serverProfileDom1 = await ServerProfileDom.Create(testApp);

        // farm1
        using var farm1 = await ServerFarmDom.Create(testApp, new ServerFarmCreateParams {
            ServerProfileId = serverProfileDom1.ServerProfileId
        }, serverCount: 0);
        await farm1.AddNewServer();
        await farm1.AddNewServer();
        await farm1.AddNewServer();

        // farm2
        await ServerFarmDom.Create(testApp, new ServerFarmCreateParams {
            ServerProfileId = serverProfileDom1.ServerProfileId
        }, serverCount: 0);

        var data = await serverProfileDom1.Reload();
        Assert.AreEqual(2, data.Summary?.ServerFarmCount);
        Assert.AreEqual(3, data.Summary?.ServerCount);
    }
}