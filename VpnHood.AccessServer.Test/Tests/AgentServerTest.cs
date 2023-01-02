using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VpnHood.Common.Client;
using VpnHood.Common.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.ServerUtils;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Messaging;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AgentServerTest : BaseTest
{
    private class TestServer
    {
        public TestServer(TestInit testInit, Guid groupId, bool configure = true, bool sendStatus = true,
            IPEndPoint? serverEndPoint = null)
        {
            ServerEndPoint = serverEndPoint ?? testInit.NewEndPoint().Result;
            Server = testInit.ServersClient.CreateAsync(testInit.ProjectId, new ServerCreateParams()).Result;
            testInit.AccessPointsClient.CreateAsync(testInit.ProjectId,
                new AccessPointCreateParams
                {
                    ServerId = Server.ServerId,
                    IpAddress = ServerEndPoint.Address.ToString(),
                    AccessPointGroupId = groupId,
                    AccessPointMode = AccessPointMode.Public,
                    TcpPort = ServerEndPoint.Port,
                    IsListen = true
                }).Wait();
            AgentClient = testInit.CreateAgentClient(Server.ServerId);
            ServerStatus.SessionCount = 0;

            if (configure)
            {
                var serverInfo = testInit.NewServerInfo().Result;
                serverInfo.Status = ServerStatus;

                var config = AgentClient.Server_Configure(serverInfo).Result;
                if (sendStatus)
                {
                    ServerStatus.ConfigCode = config.ConfigCode;
                    AgentClient.Server_UpdateStatus(serverInfo.Status).Wait();
                }
            }
        }

        public IPEndPoint ServerEndPoint { get; }
        public Api.Server Server { get; }
        public AgentClient AgentClient { get; }
        public ServerStatus ServerStatus { get; } = TestInit.NewServerStatus(null);
    }

    // return the only PublicInToken AccessPoint
    private async Task<AccessPoint?> Configure_auto_update_accessPoints_on_internal(Api.Server server)
    {
        var accessPointClient = TestInit1.AccessPointsClient;

        // create serverInfo
        var serverInfo = await TestInit1.NewServerInfo();
        var publicIp = await TestInit1.NewIpV6();
        var privateIp = await TestInit1.NewIpV4();
        serverInfo.PrivateIpAddresses = new[] { publicIp, privateIp, await TestInit1.NewIpV6(), privateIp };
        serverInfo.PublicIpAddresses = new[] { publicIp, await TestInit1.NewIpV4(), await TestInit1.NewIpV6() };

        //Configure
        var agentClient = TestInit1.CreateAgentClient(server.ServerId);
        var serverConfig = await agentClient.Server_Configure(serverInfo);
        Assert.AreEqual(TestInit1.AgentOptions.ServerUpdateStatusInterval, serverConfig.UpdateStatusInterval);
        Assert.AreEqual(serverConfig.TcpEndPoints.Length, serverConfig.TcpEndPoints.Distinct().Count(),
            "Duplicate listener!");

        //-----------
        // check: Configure with AutoUpdate is true (ServerModel.AccessPointGroupId is set)
        //-----------
        var accessPoints = (await accessPointClient.ListAsync(TestInit1.ProjectId, server.ServerId)).ToArray();
        var totalServerInfoIpAddress =
            serverInfo.PrivateIpAddresses.Concat(serverInfo.PublicIpAddresses).Distinct().Count();
        Assert.AreEqual(totalServerInfoIpAddress, accessPoints.Length);

        // private[0]
        var accessPoint =
            accessPoints.Single(x => x.IpAddress == serverInfo.PrivateIpAddresses.ToArray()[0].ToString());
        var accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken,
            "shared publicIp and privateIp must be see as publicIp");
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
        Assert.IsTrue(accessPoint.IsListen, "shared publicIp and privateIp");
        Assert.IsTrue(serverConfig.TcpEndPoints.Any(x => x.ToString() == accessEndPoint.ToString()));

        // private[1]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PrivateIpAddresses.ToArray()[1].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.AreEqual(AccessPointMode.Private, accessPoint.AccessPointMode);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
        Assert.IsTrue(accessPoint.IsListen);
        Assert.IsTrue(serverConfig.TcpEndPoints.Any(x => x.ToString() == accessEndPoint.ToString()));

        // private[2]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PrivateIpAddresses.ToArray()[2].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.AreEqual(AccessPointMode.Private, accessPoint.AccessPointMode);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
        Assert.IsTrue(accessPoint.IsListen);
        Assert.IsTrue(serverConfig.TcpEndPoints.Any(x => x.ToString() == accessEndPoint.ToString()));

        // public[0]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PublicIpAddresses.ToArray()[0].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
        Assert.IsTrue(accessPoint.IsListen, "shared publicIp and privateIp");
        Assert.IsTrue(serverConfig.TcpEndPoints.Any(x => x.ToString() == accessEndPoint.ToString()));

        // public[1]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PublicIpAddresses.ToArray()[1].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
        Assert.IsFalse(accessPoint.IsListen);
        Assert.IsFalse(serverConfig.TcpEndPoints.Any(x => x.ToString() == accessEndPoint.ToString()));

        // public[2]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PublicIpAddresses.ToArray()[2].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
        Assert.IsFalse(accessPoint.IsListen);
        Assert.IsFalse(serverConfig.TcpEndPoints.Any(x => x.ToString() == accessEndPoint.ToString()));

        // PublicInToken should never be deleted
        return accessPoints.SingleOrDefault(x => x.AccessPointMode == AccessPointMode.PublicInToken);
    }

    [TestMethod]
    public async Task Configure_off_auto_update_accessPoints()
    {
        // create serverInfo
        var serverClient = TestInit1.ServersClient;
        var server =
            await serverClient.CreateAsync(TestInit1.ProjectId, new ServerCreateParams { AccessPointGroupId = null });

        var accessPointClient = TestInit1.AccessPointsClient;
        var accessPoint1 = await accessPointClient.CreateAsync(TestInit1.ProjectId,
            new AccessPointCreateParams
            {
                ServerId = server.ServerId,
                IpAddress = await TestInit1.NewIpV4String(),
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                AccessPointMode = AccessPointMode.PublicInToken,
                IsListen = true,
                TcpPort = 4848,
                UdpPort = 150
            });

        var accessPoint2 = await accessPointClient.CreateAsync(TestInit1.ProjectId,
            new AccessPointCreateParams
            {
                ServerId = server.ServerId,
                IpAddress = await TestInit1.NewIpV4String(),
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                AccessPointMode = AccessPointMode.Private,
                IsListen = true,
                TcpPort = 5010,
                UdpPort = 0
            });

        var serverInfo1 = await TestInit1.NewServerInfo();
        var publicIp = await TestInit1.NewIpV6();
        serverInfo1.PrivateIpAddresses = new[] { publicIp, await TestInit1.NewIpV4(), await TestInit1.NewIpV6() };
        serverInfo1.PublicIpAddresses = new[] { publicIp, await TestInit1.NewIpV4(), await TestInit1.NewIpV6() };

        // Configure
        var agentClient1 = TestInit1.CreateAgentClient(server.ServerId);
        await agentClient1.Server_Configure(serverInfo1);

        // Test that accessPoints have not been changed
        var accessPoints = await accessPointClient.ListAsync(TestInit1.ProjectId, server.ServerId);
        Assert.AreEqual(2, accessPoints.Count);

        // AccessPoint1
        var expectedAccessPoint = accessPoint1;
        var actualAccessPoint = accessPoints.Single(x => x.IpAddress == expectedAccessPoint.IpAddress);
        Assert.AreEqual(expectedAccessPoint.AccessPointMode, actualAccessPoint.AccessPointMode);
        Assert.AreEqual(expectedAccessPoint.TcpPort, actualAccessPoint.TcpPort);
        Assert.AreEqual(expectedAccessPoint.UdpPort, actualAccessPoint.UdpPort);
        Assert.AreEqual(expectedAccessPoint.AccessPointGroupId, actualAccessPoint.AccessPointGroupId);
        Assert.AreEqual(expectedAccessPoint.IsListen, actualAccessPoint.IsListen);

        // AccessPoint2
        expectedAccessPoint = accessPoint2;
        actualAccessPoint = accessPoints.Single(x => x.IpAddress == expectedAccessPoint.IpAddress);
        Assert.AreEqual(expectedAccessPoint.AccessPointMode, actualAccessPoint.AccessPointMode);
        Assert.AreEqual(expectedAccessPoint.TcpPort, actualAccessPoint.TcpPort);
        Assert.AreEqual(expectedAccessPoint.UdpPort, actualAccessPoint.UdpPort);
        Assert.AreEqual(expectedAccessPoint.AccessPointGroupId, actualAccessPoint.AccessPointGroupId);
        Assert.AreEqual(expectedAccessPoint.IsListen, actualAccessPoint.IsListen);
    }

    [TestMethod]
    public async Task Configure_on_auto_update_accessPoints()
    {
        // create serverInfo
        var accessPointGroupClient = TestInit1.AccessPointGroupsClient;

        var accessPointGroup1 =
            await accessPointGroupClient.CreateAsync(TestInit1.ProjectId, new AccessPointGroupCreateParams());
        var serverClient = TestInit1.ServersClient;
        var server = await serverClient.CreateAsync(TestInit1.ProjectId,
            new ServerCreateParams { AccessPointGroupId = accessPointGroup1.AccessPointGroupId });

        var publicInTokenAccessPoint1 = await Configure_auto_update_accessPoints_on_internal(server);
        var publicInTokenAccessPoint2 = await Configure_auto_update_accessPoints_on_internal(server);

        // --------
        // Check: The only PublicInToken should be changed by second configure
        // --------
        Assert.IsNotNull(publicInTokenAccessPoint1);
        Assert.IsNotNull(publicInTokenAccessPoint2);
        Assert.AreNotEqual(publicInTokenAccessPoint1.IpAddress, publicInTokenAccessPoint2.IpAddress);

        // --------
        // Check: Keep last server tokenAccessPoint if publicIp is same
        // --------

        // create serverInfo
        var serverInfo = await TestInit1.NewServerInfo();
        serverInfo.PrivateIpAddresses = new[] { await TestInit1.NewIpV4(), await TestInit1.NewIpV6() };
        serverInfo.PublicIpAddresses = new[]
        {
            await TestInit1.NewIpV4(), await TestInit1.NewIpV6(), IPAddress.Parse(publicInTokenAccessPoint2.IpAddress)
        };

        //Configure
        var agentClient = TestInit1.CreateAgentClient(server.ServerId);
        await agentClient.Server_Configure(serverInfo);
        var accessPointClient = TestInit1.AccessPointsClient;
        var accessPoints = await accessPointClient.ListAsync(TestInit1.ProjectId, server.ServerId);
        Assert.AreEqual(publicInTokenAccessPoint2.IpAddress,
            accessPoints.Single(x => x.AccessPointMode == AccessPointMode.PublicInToken).IpAddress);

        // --------
        // Check: another server with same group should not have any PublicInTokenAccess
        // --------
        server = await serverClient.CreateAsync(TestInit1.ProjectId,
            new ServerCreateParams { AccessPointGroupId = accessPointGroup1.AccessPointGroupId });
        var publicInTokenAccessPoint = await Configure_auto_update_accessPoints_on_internal(server);
        Assert.IsNull(publicInTokenAccessPoint);

        // --------
        // Check: another server with different group should have one PublicInTokenAccess
        // --------
        var accessPointGroup2 =
            await accessPointGroupClient.CreateAsync(TestInit1.ProjectId, new AccessPointGroupCreateParams());
        server = await serverClient.CreateAsync(TestInit1.ProjectId,
            new ServerCreateParams { AccessPointGroupId = accessPointGroup2.AccessPointGroupId });
        publicInTokenAccessPoint = await Configure_auto_update_accessPoints_on_internal(server);
        Assert.IsNotNull(publicInTokenAccessPoint);
    }

    [TestMethod]
    public async Task Update_Tracking()
    {
        var farm = await AccessPointGroupDom.Create();

        await farm.TestInit.ProjectsClient.UpdateAsync(farm.ProjectId, new ProjectUpdateParams
        {
            TrackClientIp = new PatchOfBoolean { Value = false },
            TrackClientRequest = new PatchOfTrackClientRequest { Value = TrackClientRequest.Nothing }
        });
        await Task.Delay(110);
        await farm.DefaultServer.Configure();
        var trackingOptions = farm.DefaultServer.ServerConfig.TrackingOptions;
        Assert.AreEqual(trackingOptions.TrackClientIp, false);
        Assert.AreEqual(trackingOptions.TrackLocalPort, false);
        Assert.AreEqual(trackingOptions.TrackDestinationPort, false);
        Assert.AreEqual(trackingOptions.TrackDestinationIp, false);

        await farm.TestInit.ProjectsClient.UpdateAsync(farm.ProjectId, new ProjectUpdateParams
        {
            TrackClientIp = new PatchOfBoolean { Value = true },
            TrackClientRequest = new PatchOfTrackClientRequest { Value = TrackClientRequest.LocalPort }
        });
        await Task.Delay(110);
        await farm.DefaultServer.Configure();
        trackingOptions = farm.DefaultServer.ServerConfig.TrackingOptions;
        Assert.AreEqual(trackingOptions.TrackClientIp, true);
        Assert.AreEqual(trackingOptions.TrackLocalPort, true);
        Assert.AreEqual(trackingOptions.TrackDestinationPort, false);
        Assert.AreEqual(trackingOptions.TrackDestinationIp, false);

        await farm.TestInit.ProjectsClient.UpdateAsync(farm.ProjectId, new ProjectUpdateParams
        {
            TrackClientRequest = new PatchOfTrackClientRequest { Value = TrackClientRequest.LocalPortAndDstPort }
        });
        await Task.Delay(110);
        await farm.DefaultServer.Configure();
        trackingOptions = farm.DefaultServer.ServerConfig.TrackingOptions;
        Assert.AreEqual(trackingOptions.TrackClientIp, true);
        Assert.AreEqual(trackingOptions.TrackLocalPort, true);
        Assert.AreEqual(trackingOptions.TrackDestinationPort, true);
        Assert.AreEqual(trackingOptions.TrackDestinationIp, false);

        await farm.TestInit.ProjectsClient.UpdateAsync(farm.ProjectId, new ProjectUpdateParams
        {
            TrackClientRequest = new PatchOfTrackClientRequest { Value = TrackClientRequest.LocalPortAndDstPortAndDstIp }
        });
        await Task.Delay(110);
        await farm.DefaultServer.Configure();
        trackingOptions = farm.DefaultServer.ServerConfig.TrackingOptions;
        Assert.AreEqual(trackingOptions.TrackClientIp, true);
        Assert.AreEqual(trackingOptions.TrackLocalPort, true);
        Assert.AreEqual(trackingOptions.TrackDestinationPort, true);
        Assert.AreEqual(trackingOptions.TrackDestinationIp, true);
    }

    [TestMethod]
    public async Task Configure()
    {
        // create serverInfo
        var serverClient = TestInit1.ServersClient;
        var serverId = (await serverClient.CreateAsync(TestInit1.ProjectId,
            new ServerCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1 })).ServerId;
        var dateTime = DateTime.UtcNow.AddSeconds(-1);

        // create serverInfo
        var agentClient1 = TestInit1.CreateAgentClient(serverId);
        var serverInfo1 = await TestInit1.NewServerInfo();
        var publicIp = await TestInit1.NewIpV6();
        serverInfo1.PrivateIpAddresses = new[] { publicIp, (await TestInit1.NewIpV4()), (await TestInit1.NewIpV6()) };
        serverInfo1.PublicIpAddresses = new[] { publicIp, (await TestInit1.NewIpV4()), (await TestInit1.NewIpV6()) };

        //Configure
        await agentClient1.Server_Configure(serverInfo1);
        await TestInit1.Sync();
        var serverConfig = await agentClient1.Server_Configure(serverInfo1); // last status will not be synced
        await TestInit1.Sync();

        var serverData = await serverClient.GetAsync(TestInit1.ProjectId, serverId);
        var server = serverData.Server;
        var serverStatusEx = serverData.Server.ServerStatus;

        Assert.AreEqual(serverId, server.ServerId);
        Assert.AreEqual(serverInfo1.Version.ToString(), server.Version);
        Assert.AreEqual(serverInfo1.EnvironmentVersion.ToString(), server.EnvironmentVersion ?? "0.0.0");
        Assert.AreEqual(serverInfo1.OsInfo, server.OsInfo);
        Assert.AreEqual(serverInfo1.MachineName, server.MachineName);
        Assert.AreEqual(serverInfo1.TotalMemory, server.TotalMemory);
        Assert.AreEqual(serverInfo1.LogicalCoreCount, server.LogicalCoreCount);
        Assert.IsTrue(dateTime <= server.ConfigureTime);
        Assert.IsNotNull(serverStatusEx);

        Assert.AreEqual(serverInfo1.Status.AvailableMemory, serverStatusEx.AvailableMemory);
        Assert.AreEqual(ServerState.Configuring, server.ServerState);
        Assert.AreEqual(serverInfo1.Status.TcpConnectionCount, serverStatusEx.TcpConnectionCount);
        Assert.AreEqual(serverInfo1.Status.UdpConnectionCount, serverStatusEx.UdpConnectionCount);
        Assert.AreEqual(serverInfo1.Status.SessionCount, serverStatusEx.SessionCount);
        Assert.AreEqual(serverInfo1.Status.ThreadCount, serverStatusEx.ThreadCount);
        Assert.AreEqual(serverInfo1.Status.CpuUsage, serverStatusEx.CpuUsage);
        Assert.AreEqual(serverInfo1.Status.TunnelSendSpeed, serverStatusEx.TunnelSendSpeed);
        Assert.AreEqual(serverInfo1.Status.TunnelReceiveSpeed, serverStatusEx.TunnelReceiveSpeed);
        Assert.IsTrue(dateTime <= serverStatusEx.CreatedTime);

        //-----------
        // check: Check ServerStatus log is inserted
        //-----------
        var serverStatus = TestInit.NewServerStatus(serverConfig.ConfigCode);

        dateTime = DateTime.UtcNow;
        await Task.Delay(500);
        await agentClient1.Server_UpdateStatus(serverStatus);
        await TestInit1.Sync();
        await agentClient1.Server_UpdateStatus(serverStatus); // last status will not be synced
        await TestInit1.Sync();

        serverData = await serverClient.GetAsync(TestInit1.ProjectId, serverId);
        server = serverData.Server;
        Assert.AreEqual(serverStatus.AvailableMemory, server.ServerStatus?.AvailableMemory);
        Assert.AreNotEqual(ServerState.Configuring, server.ServerState);
        Assert.AreEqual(serverStatus.TcpConnectionCount, server.ServerStatus?.TcpConnectionCount);
        Assert.AreEqual(serverStatus.UdpConnectionCount, server.ServerStatus?.UdpConnectionCount);
        Assert.AreEqual(serverStatus.SessionCount, server.ServerStatus?.SessionCount);
        Assert.AreEqual(serverStatus.ThreadCount, server.ServerStatus?.ThreadCount);
        Assert.IsTrue(server.ServerStatus?.CreatedTime > dateTime);
    }


    [TestMethod]
    public async Task Reconfig()
    {
        var serverClient = TestInit1.ServersClient;
        var agentClient = TestInit1.CreateAgentClient();

        var serverId = TestInit1.ServerId1;
        var oldCode = TestInit1.ServerInfo1.Status.ConfigCode;

        //-----------
        // check
        //-----------
        await serverClient.UpdateAsync(TestInit1.ProjectId, serverId,
            new ServerUpdateParams
            { AccessPointGroupId = new PatchOfNullableGuid { Value = TestInit1.AccessPointGroupId2 } });
        await serverClient.UpdateAsync(TestInit1.ProjectId, serverId,
            new ServerUpdateParams { AccessPointGroupId = new PatchOfNullableGuid { Value = null } });
        var serverCommand = await TestInit1.AgentClient1.Server_UpdateStatus(new ServerStatus { ConfigCode = oldCode });
        Assert.AreNotEqual(oldCode, serverCommand.ConfigCode,
            "Updating AccessPointGroupId should lead to a new ConfigCode");
        oldCode = serverCommand.ConfigCode;

        //-----------
        // check
        //-----------
        var accessPointClient = TestInit1.AccessPointsClient;
        var accessPoint = await accessPointClient.CreateAsync(TestInit1.ProjectId,
            new AccessPointCreateParams
            {
                ServerId = serverId,
                IpAddress = await TestInit1.NewIpV4String(),
                AccessPointGroupId = TestInit1.AccessPointGroupId2,
                IsListen = true
            });
        serverCommand = await TestInit1.AgentClient1.Server_UpdateStatus(new ServerStatus { ConfigCode = oldCode });
        Assert.AreNotEqual(oldCode, serverCommand.ConfigCode,
            "add an AccessPoint should lead to a new ConfigCode.");
        oldCode = serverCommand.ConfigCode;

        //-----------
        // check
        //-----------
        await accessPointClient.UpdateAsync(TestInit1.ProjectId, accessPoint.AccessPointId,
            new AccessPointUpdateParams { IsListen = new PatchOfBoolean { Value = !accessPoint.IsListen } });
        var serverStatus = new ServerStatus { ConfigCode = oldCode };
        serverCommand = await TestInit1.AgentClient1.Server_UpdateStatus(serverStatus);
        Assert.AreNotEqual(oldCode, serverCommand.ConfigCode,
            "updating AccessPoint should lead to a new ConfigCode.");
        oldCode = serverCommand.ConfigCode;

        //-----------
        // check
        //-----------
        await agentClient.Server_Configure(await TestInit1.NewServerInfo());
        var serverModel = await TestInit1.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == serverId);
        Assert.AreEqual(serverStatus.ConfigCode, serverModel.LastConfigCode.ToString(),
            "LastConfigCode should be set by Server_UpdateStatus.");

        Assert.AreEqual(oldCode, serverModel.ConfigCode.ToString(),
            "ConfigCode should not be changed by ConfigureServer.");

        Assert.AreNotEqual(serverModel.LastConfigCode, serverModel.ConfigCode,
            "LastConfigCode should be changed after UpdateStatus.");

        oldCode = serverCommand.ConfigCode;

        //-----------
        // check
        //-----------
        serverStatus = new ServerStatus { ConfigCode = Guid.NewGuid().ToString() };
        await TestInit1.AgentClient1.Server_UpdateStatus(serverStatus);
        serverModel = await TestInit1.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == serverId);
        Assert.AreEqual(serverStatus.ConfigCode, serverModel.LastConfigCode.ToString(),
            "LastConfigCode should be changed even by incorrect ConfigCode");
        Assert.AreEqual(oldCode, serverModel.ConfigCode.ToString(),
            "ConfigCode should not be changed when there is no update");

        //-----------
        // check
        //-----------
        await TestInit1.AgentClient1.Server_UpdateStatus(new ServerStatus { ConfigCode = oldCode });
        serverModel = await TestInit1.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == serverId);
        Assert.AreEqual(serverModel.ConfigCode, serverModel.LastConfigCode,
            "LastConfigCode should be changed correct ConfigCode");

        //-----------
        // check Reconfig After Config finish
        //-----------
        await accessPointClient.UpdateAsync(TestInit1.ProjectId, accessPoint.AccessPointId,
            new AccessPointUpdateParams { UdpPort = new PatchOfInteger { Value = 9090 } });
        var serverData = await TestInit1.ServersClient.GetAsync(TestInit1.ProjectId, serverId);
        Assert.AreEqual(ServerState.Configuring, serverData.Server.ServerState);
    }

    [TestMethod]
    public async Task LoadBalancer()
    {
        var testInit = await TestInit.Create();
        testInit.AgentOptions.AllowRedirect = true;
        var accessPointGroup =
            await testInit.AccessPointGroupsClient.CreateAsync(testInit.ProjectId, new AccessPointGroupCreateParams());

        // Create and init servers
        var testServers = new List<TestServer>();
        for (var i = 0; i < 4; i++)
        {
            var testServer = new TestServer(testInit, accessPointGroup.AccessPointGroupId, i != 3);
            testServers.Add(testServer);
        }

        testServers.Add(new TestServer(testInit, accessPointGroup.AccessPointGroupId, true, true,
            await testInit.NewEndPointIp6()));
        testServers.Add(new TestServer(testInit, accessPointGroup.AccessPointGroupId, true, false));

        // create access token
        var accessToken = await testInit.AccessTokensClient.CreateAsync(testInit.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = accessPointGroup.AccessPointGroupId,
            });

        // create sessions
        var agentClient = testInit.CreateAgentClient(testServers[0].Server.ServerId);
        for (var i = 0; i < 9; i++)
        {
            var testServer = testServers[0];
            var sessionRequestEx =
                testInit.CreateSessionRequestEx(accessToken, hostEndPoint: testServer.ServerEndPoint);
            var sessionResponseEx = await agentClient.Session_Create(sessionRequestEx);
            if (sessionResponseEx.ErrorCode == SessionErrorCode.RedirectHost)
            {
                Assert.IsNotNull(sessionResponseEx.RedirectHostEndPoint);
                sessionRequestEx.HostEndPoint = sessionResponseEx.RedirectHostEndPoint!;
                testServer = testServers.First(x =>
                    sessionResponseEx.RedirectHostEndPoint!.Equals(x.ServerEndPoint));
                sessionResponseEx = await testServer.AgentClient.Session_Create(sessionRequestEx);
            }

            Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode, sessionResponseEx.ErrorMessage);
            testServer.ServerStatus.SessionCount++;
            await testServer.AgentClient.Server_UpdateStatus(testServer.ServerStatus);
        }

        // some server should not be selected
        Assert.AreEqual(0, testServers[3].ServerStatus.SessionCount, "A server with configuring state is selected.");
        Assert.AreEqual(0, testServers[4].ServerStatus.SessionCount, "IpVersion is not respected.");
        Assert.AreEqual(0, testServers[5].ServerStatus.SessionCount, "Should not use server in Configuring state.");

        // each server sessions must be 3
        Assert.AreEqual(3, testServers[0].ServerStatus.SessionCount);
        Assert.AreEqual(3, testServers[1].ServerStatus.SessionCount);
        Assert.AreEqual(3, testServers[2].ServerStatus.SessionCount);
    }

    [TestMethod]
    public async Task Fail_Configure_by_old_version()
    {
        // create serverInfo
        var serverClient = TestInit1.ServersClient;
        var server = await serverClient.CreateAsync(TestInit1.ProjectId,
            new ServerCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1 });

        // create serverInfo
        var agentClient1 = TestInit1.CreateAgentClient(server.ServerId);
        var serverInfo1 = await TestInit1.NewServerInfo();
        var publicIp = await TestInit1.NewIpV6();
        serverInfo1.PrivateIpAddresses = new[] { publicIp, (await TestInit1.NewIpV4()), (await TestInit1.NewIpV6()) };
        serverInfo1.PublicIpAddresses = new[] { publicIp, (await TestInit1.NewIpV4()), (await TestInit1.NewIpV6()) };

        //Configure
        serverInfo1.Version = Version.Parse("0.0.1");
        try
        {
            await agentClient1.Server_Configure(serverInfo1);
            Assert.Fail("NotSupportedException was expected.");
        }
        catch (ApiException e)
        {
            var serverData = await serverClient.GetAsync(TestInit1.ProjectId, server.ServerId);
            Assert.AreEqual(nameof(NotSupportedException), e.ExceptionTypeName);
            Assert.IsTrue(serverData.Server.LastConfigError?.Contains("version", StringComparison.OrdinalIgnoreCase));
            serverInfo1.LastError = serverData.Server.LastConfigError;
        }

        // LastConfigError must be removed after successful configuration
        serverInfo1.Version = ServerUtil.MinServerVersion;
        var configure = await agentClient1.Server_Configure(serverInfo1);
        await agentClient1.Server_UpdateStatus(TestInit.NewServerStatus(configure.ConfigCode));
        var serverData2 = await serverClient.GetAsync(TestInit1.ProjectId, server.ServerId);
        Assert.IsNull(serverData2.Server.LastConfigError);
    }

    [TestMethod]
    public async Task Server_AutoConfigMemory()
    {
        const long gb = 0x40000000;

        var sampler = await AccessPointGroupDom.Create(serverCount: 0);
        var sampleServer = await sampler.AddNewServer(false);

        sampleServer.ServerInfo.TotalMemory = 60L * gb;
        await sampleServer.Configure();
        Assert.AreEqual(8192, sampleServer.ServerConfig.SessionOptions.TcpBufferSize);

        //sampleServer.ServerInfo.TotalMemory = 2L * gb;
        //await sampleServer.Configure();
        //Assert.AreEqual(8192, sampleServer.ServerConfig.SessionOptions.TcpBufferSize);

        //sampleServer.ServerInfo.TotalMemory = 4L * gb;
        //await sampleServer.Configure();
        //Assert.AreEqual(8192, sampleServer.ServerConfig.SessionOptions.TcpBufferSize);

        //sampleServer.ServerInfo.TotalMemory = 7L * gb;
        //await sampleServer.Configure();
        //Assert.AreEqual(8192 * 2, sampleServer.ServerConfig.SessionOptions.TcpBufferSize);

        //sampleServer.ServerInfo.TotalMemory = 64L * gb;
        //await sampleServer.Configure();
        //Assert.AreEqual(81920, sampleServer.ServerConfig.SessionOptions.TcpBufferSize);

        //sampleServer.ServerInfo.TotalMemory = 128L * gb;
        //await sampleServer.Configure();
        //Assert.AreEqual(81920, sampleServer.ServerConfig.SessionOptions.TcpBufferSize);
    }

    [TestMethod]
    public async Task ServerStatus_recovery_by_cache()
    {
        var sampler = await AccessPointGroupDom.Create(serverCount: 1);
        var server = await sampler.AddNewServer();

        // Clear Cache
        await sampler.TestInit.FlushCache();
        await sampler.TestInit.AgentCacheClient.InvalidateProject(sampler.ProjectId);

        // update status again
        await server.UpdateStatus(server.ServerInfo.Status);
        var servers = await sampler.TestInit.AgentCacheClient.GetServers(sampler.ProjectId);
        Assert.IsTrue(servers.Any(x => x.ServerId == server.ServerId));
    }

    [TestMethod]
    public async Task GetCertificateData()
    {
        // create new AccessPoint
        var privateEp = new IPEndPoint(await TestInit1.NewIpV4(), 4443);
        var publicEp1 = new IPEndPoint(await TestInit1.NewIpV4(), 4443);
        var publicEp2 = new IPEndPoint(await TestInit1.NewIpV4(), 4443);
        var accessPointClient = TestInit1.AccessPointsClient;
        await accessPointClient.CreateAsync(TestInit1.ProjectId,
            new AccessPointCreateParams
            {
                ServerId = TestInit1.ServerId1,
                IpAddress = publicEp1.Address.ToString(),
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                TcpPort = publicEp1.Port,
                AccessPointMode = AccessPointMode.Public,
                IsListen = true
            });

        await accessPointClient.CreateAsync(TestInit1.ProjectId,
            new AccessPointCreateParams
            {
                ServerId = TestInit1.ServerId1,
                IpAddress = publicEp2.Address.ToString(),
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                TcpPort = publicEp2.Port,
                AccessPointMode = AccessPointMode.PublicInToken,
                IsListen = false
            });

        await accessPointClient.CreateAsync(TestInit1.ProjectId,
            new AccessPointCreateParams
            {
                ServerId = TestInit1.ServerId1,
                IpAddress = privateEp.Address.ToString(),
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                TcpPort = privateEp.Port,
                AccessPointMode = AccessPointMode.Private,
                IsListen = true
            });

        //-----------
        // check: get certificate by publicIp
        //-----------
        var agentClient = TestInit1.CreateAgentClient();
        var certBuffer = await agentClient.GetSslCertificateData(publicEp1);
        var certificate = new X509Certificate2(certBuffer);
        Assert.AreEqual(TestInit1.PublicServerDns, certificate.GetNameInfo(X509NameType.DnsName, false));

        //-----------
        // check: get certificate by privateIp
        //-----------
        certBuffer = await agentClient.GetSslCertificateData(privateEp);
        certificate = new X509Certificate2(certBuffer);
        Assert.AreEqual(TestInit1.PublicServerDns, certificate.GetNameInfo(X509NameType.DnsName, false));

        //-----------
        // check: check not found
        //-----------
        try
        {
            await agentClient.GetSslCertificateData(publicEp2);
            Assert.Fail("NotExistsException expected!");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }
    }

    [TestMethod]
    public async Task Server_UpdateStatus()
    {
        var farm = await AccessPointGroupDom.Create(serverCount: 0);
        var testInit = farm.TestInit;
        var serverDom1 = await farm.AddNewServer();
        var serverDom2 = await farm.AddNewServer();

        await serverDom1.UpdateStatus(new ServerStatus { SessionCount = 1 });
        await serverDom1.UpdateStatus(new ServerStatus { SessionCount = 2 });
        await serverDom1.UpdateStatus(new ServerStatus { SessionCount = 3 });
        await serverDom1.UpdateStatus(new ServerStatus { SessionCount = 4, AvailableMemory = 100, CpuUsage = 2 });
        await testInit.FlushCache();

        await serverDom1.UpdateStatus(new ServerStatus { SessionCount = 9 });
        await serverDom1.UpdateStatus(new ServerStatus { SessionCount = 10 });
        await serverDom2.UpdateStatus(new ServerStatus { SessionCount = 19 });
        await serverDom2.UpdateStatus(new ServerStatus { SessionCount = 20 });

        var serverData1 = await testInit.ServersClient.GetAsync(testInit.ProjectId, serverDom1.ServerId);
        Assert.AreEqual(serverData1.Server.ServerStatus?.SessionCount, 10);

        var serverData2 = await testInit.ServersClient.GetAsync(testInit.ProjectId, serverDom2.ServerId);
        Assert.AreEqual(serverData2.Server.ServerStatus?.SessionCount, 20);

        await testInit.FlushCache();

        // check saving cache
        var serverStatus = await testInit.VhContext.ServerStatuses
            .Where(x => x.ServerId == serverDom1.ServerId || x.ServerId == serverDom2.ServerId)
            .ToArrayAsync();

        var status4 = serverStatus.Single(x => x.ServerId == serverDom1.ServerId && x.SessionCount == 4);
        Assert.AreEqual((byte?)2, status4.CpuUsage);
        Assert.AreEqual(100, status4.AvailableMemory);

        Assert.IsTrue(serverStatus.Any(x =>
            x.ServerId == serverDom2.ServerId &&
            x.SessionCount == 20), "Status has not been saved!");
    }
}