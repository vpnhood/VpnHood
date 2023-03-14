using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.Common.Client;
using VpnHood.Common.Exceptions;
using VpnHood.Server.Configurations;
using VpnHood.AccessServer.Test.Dom;
using System;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ServerProfileTest
{
    [TestMethod]
    public async Task Default_ServerProfile()
    {
        var testInit = await TestInit.Create();

        // -----------
        // Make sure default is created
        // -----------
        var serverProfiles = await testInit.ServerProfilesClient.ListAsync(testInit.ProjectId);
        var defaultServerProfile = serverProfiles.Single(x => x.ServerProfile.IsDefault).ServerProfile;
        Assert.IsNotNull(defaultServerProfile);

        var serverProfileDom = new ServerProfileDom(testInit, defaultServerProfile);

        // -----------
        // Default can not be deleted
        // -----------
        try
        {
            await serverProfileDom.Delete();
            Assert.Fail("Default ServerProfile should not be deletable.");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(ex.ExceptionTypeName, nameof(InvalidOperationException));
        }

        // -----------
        // Default can not be renamed
        // -----------
        try
        {
            await serverProfileDom.Update(new ServerProfileUpdateParams
            {
                ServerProfileName = new PatchOfString { Value = Guid.NewGuid().ToString() }
            });
            Assert.Fail("Default ServerProfile should not be deletable.");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(ex.ExceptionTypeName, nameof(InvalidOperationException));
        }
    }

    [TestMethod]
    public async Task Crud()
    {
        var testInit = await TestInit.Create();

        // -----------
        // Create
        // -----------
        // ReSharper disable once UseObjectOrCollectionInitializer
        var serverConfig = new ServerConfig();
        serverConfig.SessionOptions.NetScanLimit = 1000;
        var serverProfileDom = await ServerProfileDom.Create(testInit, new ServerProfileCreateParams
        {
            ServerConfig = JsonSerializer.Serialize(serverConfig)
        });
        Assert.IsNotNull(serverProfileDom.ServerProfile.ServerConfig);
        var serverConfig2 = VhUtil.JsonDeserialize<ServerConfig>(serverProfileDom.ServerProfile.ServerConfig);
        Assert.AreEqual(serverConfig.SessionOptions.NetScanLimit, serverConfig2.SessionOptions.NetScanLimit);

        // -----------
        // get
        // -----------
        await serverProfileDom.Reload();
        serverConfig2 = VhUtil.JsonDeserialize<ServerConfig>(serverProfileDom.ServerProfile.ServerConfig);
        Assert.AreEqual(serverConfig.SessionOptions.NetScanLimit, serverConfig2.SessionOptions.NetScanLimit);

        // -----------
        // update
        // -----------
        serverConfig.SessionOptions.NetScanLimit = 2000;
        await serverProfileDom.Update(new ServerProfileUpdateParams
        {
            ServerConfig = new PatchOfString { Value = JsonSerializer.Serialize(serverConfig) }
        });
        serverConfig2 = VhUtil.JsonDeserialize<ServerConfig>(serverProfileDom.ServerProfile.ServerConfig);
        Assert.AreEqual(serverConfig.SessionOptions.NetScanLimit, serverConfig2.SessionOptions.NetScanLimit);

        // -----------
        // Delete
        // -----------
        await serverProfileDom.Delete();
        try
        {
            await serverProfileDom.Reload();
            Assert.Fail("NotExistsException was expected");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Reconfigure_all_servers_on_update()
    {
        var testInit = await TestInit.Create();
        var serverProfileDom = await ServerProfileDom.Create(testInit);

        // farm1
        var farm1 = await ServerFarmDom.Create(testInit, new ServerFarmCreateParams
        {
            ServerProfileId = serverProfileDom.ServerProfileId
        });
        var serverDom1 =  await farm1.AddNewServer();
        var serverDom2 = await farm1.AddNewServer();

        // farm2
        var farm2 = await ServerFarmDom.Create(testInit, new ServerFarmCreateParams
        {
            ServerProfileId = serverProfileDom.ServerProfileId
        });
        var serverDom3= await farm2.AddNewServer();
        var serverDom4 = await farm2.AddNewServer();

        // update ServerProfile
        var config = new ServerConfig { UpdateStatusInterval = TimeSpan.FromSeconds(151) };
        await serverProfileDom.Update(new ServerProfileUpdateParams
        {
            ServerConfig = new PatchOfString {Value = JsonSerializer.Serialize(config) }
        });

        // check serverConfig
        Assert.AreNotEqual(serverDom1.ServerStatus.ConfigCode, (await serverDom1.SendStatus()).ConfigCode);
        Assert.AreNotEqual(serverDom2.ServerStatus.ConfigCode, (await serverDom2.SendStatus()).ConfigCode);
        Assert.AreNotEqual(serverDom3.ServerStatus.ConfigCode, (await serverDom3.SendStatus()).ConfigCode);
        Assert.AreNotEqual(serverDom4.ServerStatus.ConfigCode, (await serverDom4.SendStatus()).ConfigCode);
    }

}