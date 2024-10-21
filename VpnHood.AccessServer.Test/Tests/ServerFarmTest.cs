using GrayMint.Common.Exceptions;
using GrayMint.Common.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common;
using VpnHood.Common.ApiClients;
using VpnHood.Common.Utils;
using Token = VpnHood.Common.Token;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class ServerFarmTest
{
    [TestMethod]
    public async Task Crud()
    {
        using var testApp = await TestApp.Create();
        var farm1 = await ServerFarmDom.Create(testApp, serverCount: 0, createParams: new ServerFarmCreateParams());
        Assert.IsTrue(farm1.ServerFarm.PushTokenToClient);

        var serverDom = await farm1.AddNewServer();
        await farm1.CreateAccessToken(true);
        await farm1.CreateAccessToken(true);

        //-----------
        // check: create
        //-----------
        var publicIp1 = testApp.NewIpV4();
        var publicIp2 = testApp.NewIpV4();
        await serverDom.Update(new ServerUpdateParams {
            AccessPoints = new PatchOfAccessPointOf {
                Value = [
                    new AccessPoint {
                        AccessPointMode = AccessPointMode.PublicInToken,
                        IpAddress = publicIp1.ToString(),
                        TcpPort = 443,
                        IsListen = true,
                        UdpPort = 443
                    },
                    new AccessPoint {
                        AccessPointMode = AccessPointMode.PublicInToken,
                        IpAddress = publicIp2.ToString(),
                        TcpPort = 443,
                        IsListen = true,
                        UdpPort = 443
                    }
                ]
            }
        });

        var accessFarmData = await farm1.Reload();
        Assert.AreEqual(farm1.ServerFarm.ServerFarmName, accessFarmData.ServerFarm.ServerFarmName);
        Assert.AreEqual(1, accessFarmData.Summary!.ServerCount);
        Assert.AreEqual(2, accessFarmData.Summary!.TotalTokenCount);
        Assert.AreEqual(2, accessFarmData.Summary!.UnusedTokenCount);
        Assert.AreEqual(0, accessFarmData.Summary!.InactiveTokenCount);
        Assert.AreEqual(16, accessFarmData.ServerFarm.Secret.Length);

        var accessTokenDom = await farm1.CreateAccessToken(true);
        var accessKey = await accessTokenDom.GetAccessKey();
        var token = Token.FromAccessKey(accessKey);
        Assert.IsTrue(token.ServerToken.HostEndPoints!.Any(x => x.Address.Equals(publicIp1)));
        Assert.IsTrue(token.ServerToken.HostEndPoints!.Any(x => x.Address.Equals(publicIp2)));

        //-----------
        // check: update 
        //-----------
        var serverProfile2 = await ServerProfileDom.Create(testApp);

        var updateParam = new ServerFarmUpdateParams {
            ServerProfileId = new PatchOfGuid { Value = serverProfile2.ServerProfileId },
            ServerFarmName = new PatchOfString { Value = $"groupName_{Guid.NewGuid()}" },
            Secret = new PatchOfByteOf { Value = GmUtil.GenerateKey() },
            PushTokenToClient = new PatchOfBoolean { Value = true }
        };

        await testApp.ServerFarmsClient.UpdateAsync(farm1.ProjectId, farm1.ServerFarmId, updateParam);
        await farm1.Reload();
        Assert.AreEqual(updateParam.ServerFarmName.Value, farm1.ServerFarm.ServerFarmName);
        Assert.AreEqual(updateParam.ServerProfileId.Value, farm1.ServerFarm.ServerProfileId);
        Assert.IsTrue(farm1.ServerFarm.PushTokenToClient);
        CollectionAssert.AreEqual(updateParam.Secret.Value, farm1.ServerFarm.Secret);

        //-----------
        // check: AlreadyExists exception
        //-----------
        try {
            await ServerFarmDom.Create(testApp,
                new ServerFarmCreateParams {
                    ServerFarmName = farm1.ServerFarm.ServerFarmName
                });
            Assert.Fail("Exception Expected!");
        }
        catch (ApiException ex) {
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
        //await farm1.TestApp.FlushCache();

        // remove server from farm
        var farm2 = await ServerFarmDom.Create(farm1.TestApp);
        await farm1.DefaultServer.Update(new ServerUpdateParams {
            ServerFarmId = new PatchOfGuid { Value = farm2.ServerFarmId }
        });

        // delete the server
        await farm1.Client.DeleteAsync(farm1.ProjectId, farm1.ServerFarmId);
        await VhTestUtil.AssertNotExistsException(farm1.Reload());
        await VhTestUtil.AssertNotExistsException(accessTokenDom.Reload());
    }

    [TestMethod]
    public async Task List()
    {
        var farm1 = await ServerFarmDom.Create(serverCount: 1);
        var farm2 = await ServerFarmDom.Create(farm1.TestApp, serverCount: 1);
        await farm1.DefaultServer.CreateSession((await farm1.CreateAccessToken()).AccessToken);
        await farm2.DefaultServer.CreateSession((await farm2.CreateAccessToken()).AccessToken);

        var farms = await farm1.TestApp.ServerFarmsClient.ListAsync(farm1.TestApp.ProjectId, includeSummary: false);
        Assert.AreEqual(3, farms.Count);
        Assert.IsTrue(farms.Any(x => x.ServerFarm.ServerFarmId == farm1.ServerFarmId));
        Assert.IsTrue(farms.Any(x => x.ServerFarm.ServerFarmId == farm2.ServerFarmId));
    }


    [TestMethod]
    public async Task List_with_summary()
    {
        var farm1 = await ServerFarmDom.Create(serverCount: 1);
        var farm2 = await ServerFarmDom.Create(farm1.TestApp, serverCount: 1);
        await farm1.DefaultServer.CreateSession((await farm1.CreateAccessToken()).AccessToken);
        await farm2.DefaultServer.CreateSession((await farm2.CreateAccessToken()).AccessToken);

        var farms = await farm1.TestApp.ServerFarmsClient.ListAsync(farm1.TestApp.ProjectId, includeSummary: true);
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
        var farm2 = await ServerFarmDom.Create(farm1.TestApp, serverCount: 0);
        var serverDom = await farm2.AddNewServer();
        try {
            await farm2.TestApp.ServerFarmsClient.DeleteAsync(farm2.ProjectId, farm2.ServerFarmId);
            Assert.Fail("Exception Expected!");
        }
        catch (ApiException ex) {
            Assert.AreEqual(nameof(InvalidOperationException), ex.ExceptionTypeName);
        }

        // move server to farm1
        await farm2.TestApp.ServersClient.UpdateAsync(farm2.ProjectId, serverDom.ServerId, new ServerUpdateParams {
            ServerFarmId = new PatchOfGuid { Value = farm1.ServerFarmId }
        });
        await farm2.TestApp.ServerFarmsClient.DeleteAsync(farm2.ProjectId, farm2.ServerFarmId);
        try {
            await farm2.Reload();
            Assert.Fail("Exception Expected.");
        }
        catch (ApiException ex) {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Reconfigure_all_servers_on_update_server_profile()
    {
        using var farm = await ServerFarmDom.Create();
        var serverDom1 = await farm.AddNewServer();
        var serverDom2 = await farm.AddNewServer();
        var serverProfileDom = await ServerProfileDom.Create(farm.TestApp);

        await farm.Update(new ServerFarmUpdateParams {
            ServerProfileId = new PatchOfGuid { Value = serverProfileDom.ServerProfileId }
        });

        // check serverConfig
        Assert.AreNotEqual(serverDom1.ServerStatus.ConfigCode, (await serverDom1.SendStatus()).ConfigCode);
        Assert.AreNotEqual(serverDom2.ServerStatus.ConfigCode, (await serverDom2.SendStatus()).ConfigCode);
    }

    [TestMethod]
    public async Task GetFarmToken()
    {
        using var farm = await ServerFarmDom.Create();
        var encFarmToken = await farm.Client.GetEncryptedTokenAsync(farm.ProjectId, farm.ServerFarmId);
        var accessToken = await farm.CreateAccessToken();
        var farmToken = ServerToken.Decrypt(farm.ServerFarm.Secret, encFarmToken);

        // compare server token part of token with farm token
        var token = await accessToken.GetToken();
        Assert.AreEqual(token.ServerToken.HostName, farmToken.HostName);
    }

    [TestMethod]
    public async Task FarmToken_must_change_by_modifying_certificate()
    {
        // get farm token
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        var server = await farm.AddNewServer();
        var accessTokenDom = await farm.CreateAccessToken();
        var accessKey = await accessTokenDom.GetAccessKey();
        var token1 = Token.FromAccessKey(accessKey);

        await farm.CertificateReplace();
        var accessKey2 = await accessTokenDom.GetAccessKey();
        var token2 = Token.FromAccessKey(accessKey2);

        Assert.AreNotEqual(token1.ServerToken.HostName, token2.ServerToken.HostName);

        // new session should return the updated token
        await server.Configure();
        var sessionDom = await accessTokenDom.CreateSession();
        Assert.IsNotNull(sessionDom.SessionResponseEx.AccessKey);
        var token3 = Token.FromAccessKey(sessionDom.SessionResponseEx.AccessKey);
        Assert.AreEqual(token2.ServerToken.HostName, token3.ServerToken.HostName);
    }

    [TestMethod]
    public async Task FarmToken_must_change_by_adding_server()
    {
        // get farm token
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        var server = await farm.AddNewServer();

        var accessTokenDom = await farm.CreateAccessToken();
        var accessKey = await accessTokenDom.GetAccessKey();
        var token = Token.FromAccessKey(accessKey);
        Assert.IsTrue(token.ServerToken.HostEndPoints?.Any(x =>
            x.Address.Equals(server.ServerInfo.PublicIpAddresses.First())));

        server = await farm.AddNewServer();
        server.Server.AccessPoints.First(x => x.AccessPointMode == AccessPointMode.Public).AccessPointMode =
            AccessPointMode.PublicInToken;
        await server.Update(new ServerUpdateParams {
            AccessPoints = new PatchOfAccessPointOf { Value = server.Server.AccessPoints }
        });
        await server.Configure();
        await farm.ReloadServers();

        accessTokenDom = await farm.CreateAccessToken();
        accessKey = await accessTokenDom.GetAccessKey();
        token = Token.FromAccessKey(accessKey);
        Assert.IsTrue(token.ServerToken.HostEndPoints?.Any(x =>
            x.Address.Equals(server.ServerInfo.PublicIpAddresses.First())));

        // new session should return the updated token
        var sessionDom = await accessTokenDom.CreateSession();
        Assert.IsNotNull(sessionDom.SessionResponseEx.AccessKey);
        token = Token.FromAccessKey(sessionDom.SessionResponseEx.AccessKey);
        Assert.IsTrue(token.ServerToken.HostEndPoints?.Any(x =>
            x.Address.Equals(server.ServerInfo.PublicIpAddresses.First())));
    }

    [TestMethod]
    public async Task FarmToken_should_not_have_deleted_server_ip()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        var accessPoint1 = farm.TestApp.NewAccessPoint();
        var accessPoint2 = farm.TestApp.NewAccessPoint();

        // create farm with 2 servers
        var server1 = await farm.AddNewServer(configure: false);
        var server2 = await farm.AddNewServer(configure: false);
        await server1.Update(new ServerUpdateParams {
            AccessPoints = new PatchOfAccessPointOf { Value = [accessPoint1] },
            AutoConfigure = new PatchOfBoolean { Value = false }
        });
        await server2.Update(new ServerUpdateParams {
            AccessPoints = new PatchOfAccessPointOf { Value = [accessPoint2] },
            AutoConfigure = new PatchOfBoolean { Value = false }
        });
        await server1.Configure();
        await server2.Configure();

        // get farm token
        var accessTokenDom = await farm.CreateAccessToken();
        var accessKey = await accessTokenDom.GetAccessKey();
        var token = Token.FromAccessKey(accessKey);
        Assert.IsTrue(token.ServerToken.HostEndPoints?.Any(x =>
            x.Address.ToString() == accessPoint1.IpAddress));

        // delete server1
        await server2.Delete();

        // check
        accessTokenDom = await farm.CreateAccessToken();
        accessKey = await accessTokenDom.GetAccessKey();
        token = Token.FromAccessKey(accessKey);
        Assert.IsFalse(token.ServerToken.HostEndPoints?.Any(x =>
            x.Address.Equals(server2.ServerInfo.PublicIpAddresses.First())));
    }
}