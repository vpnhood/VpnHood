using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Client;
using GrayMint.Common.Exceptions;
using VpnHood.Common;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ServerFarmTest
{
    [TestMethod]
    public async Task Crud()
    {
        var testInit = await TestInit.Create();
        var farm1 = await ServerFarmDom.Create(testInit, serverCount: 0);
        var serverDom = await farm1.AddNewServer();
        await farm1.CreateAccessToken(true);
        await farm1.CreateAccessToken(true);

        //-----------
        // check: create
        //-----------
        var publicIp1 = await testInit.NewIpV4();
        var publicIp2 = await testInit.NewIpV4();
        await serverDom.Update(new ServerUpdateParams
        {
            AccessPoints = new PatchOfAccessPointOf
            {
                Value = new[]
                {
                    new AccessPoint
                    {
                        AccessPointMode = AccessPointMode.PublicInToken,
                        IpAddress = publicIp1.ToString(),
                        TcpPort = 443,
                        IsListen = true,
                        UdpPort = 443,
                    },
                    new AccessPoint
                    {
                        AccessPointMode = AccessPointMode.PublicInToken,
                        IpAddress = publicIp2.ToString(),
                        TcpPort = 443,
                        IsListen = true,
                        UdpPort = 443,
                    }
                }
            }
        });

        var accessFarmData = await farm1.Reload();
        Assert.AreEqual(farm1.ServerFarm.ServerFarmName, farm1.ServerFarm.ServerFarmName);
        Assert.AreEqual(1, accessFarmData.Summary!.ServerCount);
        Assert.AreEqual(2, accessFarmData.Summary!.TotalTokenCount);
        Assert.AreEqual(2, accessFarmData.Summary!.UnusedTokenCount);
        Assert.AreEqual(0, accessFarmData.Summary!.InactiveTokenCount);
        Assert.AreEqual(16, accessFarmData.ServerFarm.Secret?.Length);

        var accessTokenDom = await farm1.CreateAccessToken(true);
        var accessKey = await accessTokenDom.GetAccessKey();
        var token = Token.FromAccessKey(accessKey);
        Assert.IsTrue(token.HostEndPoints!.Any(x => x.Address.Equals(publicIp1)));
        Assert.IsTrue(token.HostEndPoints!.Any(x => x.Address.Equals(publicIp2)));

        //-----------
        // check: update 
        //-----------
        var serverProfile2 = await ServerProfileDom.Create(testInit);
        var certificateClient = testInit.CertificatesClient;
        var certificate2 = await certificateClient.CreateAsync(farm1.ProjectId, new CertificateCreateParams { SubjectName = "CN=fff.com" });
        var updateParam = new ServerFarmUpdateParams
        {
            ServerProfileId = new PatchOfGuid { Value = serverProfile2.ServerProfileId },
            CertificateId = new PatchOfGuid { Value = certificate2.CertificateId },
            ServerFarmName = new PatchOfString { Value = $"groupName_{Guid.NewGuid()}" }
        };

        await testInit.ServerFarmsClient.UpdateAsync(farm1.ProjectId, farm1.ServerFarmId, updateParam);
        await farm1.Reload();
        Assert.AreEqual(updateParam.ServerFarmName.Value, farm1.ServerFarm.ServerFarmName);
        Assert.AreEqual(updateParam.CertificateId.Value, farm1.ServerFarm.CertificateId);
        Assert.AreEqual(updateParam.ServerProfileId.Value, farm1.ServerFarm.ServerProfileId);

        //-----------
        // check: AlreadyExists exception
        //-----------
        try
        {
            await ServerFarmDom.Create(testInit,
                new ServerFarmCreateParams
                {
                    ServerFarmName = farm1.ServerFarm.ServerFarmName
                });
            Assert.Fail("Exception Expected!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(AlreadyExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Delete_farm_and_its_dependents()
    {
        var farm1 = await ServerFarmDom.Create();
        var accessTokenDom = await farm1.CreateAccessToken(true);
        var session = await accessTokenDom.CreateSession();
        await session.AddUsage();
        await session.AddUsage();
        //await farm1.TestInit.FlushCache();

        // remove server from farm
        var farm2 = await ServerFarmDom.Create(farm1.TestInit);
        await farm1.DefaultServer.Update(new ServerUpdateParams { ServerFarmId = new PatchOfGuid { Value = farm2.ServerFarmId } });

        // delete the server
        await farm1.Client.DeleteAsync(farm1.ProjectId, farm1.ServerFarmId);
        try
        {
            await farm1.Reload();
            Assert.Fail("Exception Expected.");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }

        try
        {
            await accessTokenDom.Reload();
            Assert.Fail("Exception Expected.");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task List_with_summary()
    {
        var farm1 = await ServerFarmDom.Create(serverCount: 1);
        var farm2 = await ServerFarmDom.Create(farm1.TestInit, serverCount: 1);
        await farm1.DefaultServer.CreateSession((await farm1.CreateAccessToken()).AccessToken);
        await farm2.DefaultServer.CreateSession((await farm2.CreateAccessToken()).AccessToken);

        var farms = await farm1.TestInit.ServerFarmsClient.ListAsync(farm1.TestInit.ProjectId, includeSummary: true);
        Assert.AreEqual(3, farms.Count);
        Assert.IsTrue(farms.Any(x => x.ServerFarm.ServerFarmId == farm1.ServerFarmId));
        Assert.IsTrue(farms.Any(x => x.ServerFarm.ServerFarmId == farm2.ServerFarmId));
    }

    [TestMethod]
    public async Task Fail_delete_a_farm_with_server()
    {
        var farm1 = await ServerFarmDom.Create(serverCount: 0);

        //-----------
        // check: can not delete a farm with server
        //-----------
        var farm2 = await ServerFarmDom.Create(farm1.TestInit, serverCount: 0);
        var serverDom = await farm2.AddNewServer();
        try
        {
            await farm2.TestInit.ServerFarmsClient.DeleteAsync(farm2.ProjectId, farm2.ServerFarmId);
            Assert.Fail("Exception Expected!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(InvalidOperationException), ex.ExceptionTypeName);
        }

        // move server to farm1
        await farm2.TestInit.ServersClient.UpdateAsync(farm2.ProjectId, serverDom.ServerId, new ServerUpdateParams
        {
            ServerFarmId = new PatchOfGuid { Value = farm1.ServerFarmId }
        });
        await farm2.TestInit.ServerFarmsClient.DeleteAsync(farm2.ProjectId, farm2.ServerFarmId);
        try
        {
            await farm2.Reload();
            Assert.Fail("Exception Expected.");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Reconfigure_all_servers_on_update_server_profile()
    {
        var farm = await ServerFarmDom.Create();
        var serverDom1 = await farm.AddNewServer();
        var serverDom2 = await farm.AddNewServer();
        var serverProfileDom = await ServerProfileDom.Create(farm.TestInit);

        await farm.Update(new ServerFarmUpdateParams
        {
            ServerProfileId = new PatchOfGuid { Value = serverProfileDom.ServerProfileId }
        });

        // check serverConfig
        Assert.AreNotEqual(serverDom1.ServerStatus.ConfigCode, (await serverDom1.SendStatus()).ConfigCode);
        Assert.AreNotEqual(serverDom2.ServerStatus.ConfigCode, (await serverDom2.SendStatus()).ConfigCode);
    }
}