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
            Server = testInit.ServersClient.CreateAsync(testInit.ProjectId, new ServerCreateParams()
            {
                AccessPoints = new[] { testInit.NewAccessPoint(ServerEndPoint).Result }
            }).Result;
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
    private async Task<AccessPoint?> Configure_auto_update_accessPoints_on_internal(ServerDom serverDom)
    {
        // create serverInfo
        var publicIp = await TestInit1.NewIpV6();
        var privateIp = await TestInit1.NewIpV4();
        serverDom.ServerInfo.PrivateIpAddresses = new[] { publicIp, privateIp, await TestInit1.NewIpV6(), privateIp };
        serverDom.ServerInfo.PublicIpAddresses = new[] { publicIp, await TestInit1.NewIpV4(), await TestInit1.NewIpV6() };

        //Configure
        await serverDom.Configure();
        Assert.AreEqual(TestInit1.AgentOptions.ServerUpdateStatusInterval, serverDom.ServerConfig.UpdateStatusInterval);
        Assert.AreEqual(serverDom.ServerConfig.TcpEndPointsValue.Length, serverDom.ServerConfig.TcpEndPointsValue.Distinct().Count(),
            "Duplicate listener!");

        //-----------
        // check: Configure with AutoUpdate is true (ServerModel.AccessPointGroupId is set)
        //-----------
        await serverDom.Reload();
        var server = serverDom.Server;
        var accessPoints = serverDom.Server.AccessPoints.ToArray();
        var serverInfo = serverDom.ServerInfo;
        var serverConfig = serverDom.ServerConfig;
        var totalServerInfoIpAddress = serverInfo.PrivateIpAddresses.Concat(serverInfo.PublicIpAddresses).Distinct().Count();
        Assert.AreEqual(totalServerInfoIpAddress, accessPoints.Length);

        // private[0]
        var accessPoint =
            accessPoints.Single(x => x.IpAddress == serverInfo.PrivateIpAddresses.ToArray()[0].ToString());
        var accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken,
            "shared publicIp and privateIp must be see as publicIp");
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.IsTrue(accessPoint.IsListen, "shared publicIp and privateIp");
        Assert.IsTrue(serverConfig.TcpEndPointsValue.Any(x => x.ToString() == accessEndPoint.ToString()));

        // private[1]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PrivateIpAddresses.ToArray()[1].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.AreEqual(AccessPointMode.Private, accessPoint.AccessPointMode);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.IsTrue(accessPoint.IsListen);
        Assert.IsTrue(serverConfig.TcpEndPointsValue.Any(x => x.ToString() == accessEndPoint.ToString()));

        // private[2]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PrivateIpAddresses.ToArray()[2].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.AreEqual(AccessPointMode.Private, accessPoint.AccessPointMode);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.IsTrue(accessPoint.IsListen);
        Assert.IsTrue(serverConfig.TcpEndPointsValue.Any(x => x.ToString() == accessEndPoint.ToString()));

        // public[0]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PublicIpAddresses.ToArray()[0].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.IsTrue(accessPoint.IsListen, "shared publicIp and privateIp");
        Assert.IsTrue(serverConfig.TcpEndPointsValue.Any(x => x.ToString() == accessEndPoint.ToString()));

        // public[1]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PublicIpAddresses.ToArray()[1].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.IsFalse(accessPoint.IsListen);
        Assert.IsFalse(serverConfig.TcpEndPointsValue.Any(x => x.ToString() == accessEndPoint.ToString()));

        // public[2]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PublicIpAddresses.ToArray()[2].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.AreEqual(0, accessPoint.UdpPort);
        Assert.IsFalse(accessPoint.IsListen);
        Assert.IsFalse(serverConfig.TcpEndPointsValue.Any(x => x.ToString() == accessEndPoint.ToString()));

        // PublicInToken should never be deleted
        return accessPoints.SingleOrDefault(x => x.AccessPointMode == AccessPointMode.PublicInToken);
    }



    [TestMethod]
    public async Task Configure_on_auto_update_accessPoints()
    {
        // create serverInfo
        var farm = await AccessPointGroupDom.Create(serverCount: 0);
        var serverDom = await farm.AddNewServer();
        var publicInTokenAccessPoint1 = await Configure_auto_update_accessPoints_on_internal(serverDom);
        var publicInTokenAccessPoint2 = await Configure_auto_update_accessPoints_on_internal(serverDom);

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
        serverDom.ServerInfo.PrivateIpAddresses = new[] { await TestInit1.NewIpV4(), await TestInit1.NewIpV6() };
        serverDom.ServerInfo.PublicIpAddresses = new[]
        {
            await TestInit1.NewIpV4(), await TestInit1.NewIpV6(), IPAddress.Parse(publicInTokenAccessPoint2.IpAddress)
        };

        //Configure
        await serverDom.Configure();
        await serverDom.Reload();
        var accessPoints = serverDom.Server.AccessPoints.ToArray();
        Assert.AreEqual(publicInTokenAccessPoint2.IpAddress,
            accessPoints.Single(x => x.AccessPointMode == AccessPointMode.PublicInToken).IpAddress);

        // --------
        // Check: another server with same group should not have any PublicInTokenAccess
        // --------
        serverDom = await farm.AddNewServer();
        var publicInTokenAccessPoint = await Configure_auto_update_accessPoints_on_internal(serverDom);
        Assert.IsNull(publicInTokenAccessPoint);

        // --------
        // Check: another server with different group should have one PublicInTokenAccess
        // --------
        var farm2 = await AccessPointGroupDom.Create();
        serverDom = await farm2.AddNewServer();
        publicInTokenAccessPoint = await Configure_auto_update_accessPoints_on_internal(serverDom);
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
        var farm = await AccessPointGroupDom.Create();
        var dateTime = DateTime.UtcNow.AddSeconds(-1);

        // create serverInfo
        var publicIp = await TestInit1.NewIpV6();
        var serverInfo = await TestInit1.NewServerInfo();
        farm.DefaultServer.ServerInfo = serverInfo;
        serverInfo.PrivateIpAddresses = new[] { publicIp, (await TestInit1.NewIpV4()), (await TestInit1.NewIpV6()) };
        serverInfo.PublicIpAddresses = new[] { publicIp, (await TestInit1.NewIpV4()), (await TestInit1.NewIpV6()) };

        //Configure
        await farm.DefaultServer.Configure(false);
        await farm.TestInit.Sync();
        await farm.DefaultServer.Configure(false); // last status will not be synced so try again
        await TestInit1.Sync();

        await farm.DefaultServer.Reload();
        var server = farm.DefaultServer.Server;
        var serverStatusEx = farm.DefaultServer.Server.ServerStatus;

        Assert.AreEqual(serverInfo.Version.ToString(), server.Version);
        Assert.AreEqual(serverInfo.EnvironmentVersion.ToString(), server.EnvironmentVersion ?? "0.0.0");
        Assert.AreEqual(serverInfo.OsInfo, server.OsInfo);
        Assert.AreEqual(serverInfo.MachineName, server.MachineName);
        Assert.AreEqual(serverInfo.TotalMemory, server.TotalMemory);
        Assert.AreEqual(serverInfo.LogicalCoreCount, server.LogicalCoreCount);
        Assert.IsTrue(dateTime <= server.ConfigureTime);
        Assert.IsNotNull(serverStatusEx);

        Assert.AreEqual(serverInfo.Status.AvailableMemory, serverStatusEx.AvailableMemory);
        Assert.AreEqual(ServerState.Configuring, server.ServerState);
        Assert.AreEqual(serverInfo.Status.TcpConnectionCount, serverStatusEx.TcpConnectionCount);
        Assert.AreEqual(serverInfo.Status.UdpConnectionCount, serverStatusEx.UdpConnectionCount);
        Assert.AreEqual(serverInfo.Status.SessionCount, serverStatusEx.SessionCount);
        Assert.AreEqual(serverInfo.Status.ThreadCount, serverStatusEx.ThreadCount);
        Assert.AreEqual(serverInfo.Status.CpuUsage, serverStatusEx.CpuUsage);
        Assert.AreEqual(serverInfo.Status.TunnelSendSpeed, serverStatusEx.TunnelSendSpeed);
        Assert.AreEqual(serverInfo.Status.TunnelReceiveSpeed, serverStatusEx.TunnelReceiveSpeed);
        Assert.IsTrue(dateTime <= serverStatusEx.CreatedTime);

        //-----------
        // check: Check ServerStatus log is inserted
        //-----------
        var serverStatus = TestInit.NewServerStatus(null);
        await farm.DefaultServer.UpdateStatus(serverStatus);

        dateTime = DateTime.UtcNow;
        await Task.Delay(500);
        await farm.DefaultServer.UpdateStatus(serverStatus);
        await farm.TestInit.Sync();
        await farm.DefaultServer.UpdateStatus(serverStatus); // last status will not be synced
        await farm.TestInit.Sync();

        await farm.DefaultServer.Reload();
        server = farm.DefaultServer.Server;
        Assert.AreEqual(serverStatus.AvailableMemory, server.ServerStatus?.AvailableMemory);
        Assert.AreNotEqual(ServerState.Configuring, server.ServerState);
        Assert.AreEqual(serverStatus.TcpConnectionCount, server.ServerStatus?.TcpConnectionCount);
        Assert.AreEqual(serverStatus.UdpConnectionCount, server.ServerStatus?.UdpConnectionCount);
        Assert.AreEqual(serverStatus.SessionCount, server.ServerStatus?.SessionCount);
        Assert.AreEqual(serverStatus.ThreadCount, server.ServerStatus?.ThreadCount);
        Assert.IsTrue(server.ServerStatus?.CreatedTime > dateTime);
    }

    [TestMethod]
    public async Task Reconfig_by_changing_farm()
    {
        var farm1 = await AccessPointGroupDom.Create();
        var farm2 = await AccessPointGroupDom.Create();

        var oldCode = farm1.DefaultServer.ServerInfo.Status.ConfigCode;
        await farm1.DefaultServer.Update(new ServerUpdateParams { AccessPointGroupId = new PatchOfNullableGuid { Value = farm2.AccessPointGroupId } });

        var serverCommand = await TestInit1.AgentClient1.Server_UpdateStatus(new ServerStatus { ConfigCode = oldCode });
        Assert.AreNotEqual(oldCode, serverCommand.ConfigCode,
            "Updating AccessPointGroupId should lead to a new ConfigCode");
    }


    [TestMethod]
    public async Task Reconfig()
    {
        var farm = await AccessPointGroupDom.Create();
        var testInit = farm.TestInit;
        var serverDom = farm.DefaultServer;
        var oldCode = testInit.ServerInfo1.Status.ConfigCode;

        //-----------
        // check
        //-----------
        var accessPoint = await testInit.NewAccessPoint();
        await serverDom.Update(new ServerUpdateParams
        {
            AccessPoints = new PatchOfAccessPointOf { Value = new[] { accessPoint } }
        });

        var serverCommand = await testInit.AgentClient1.Server_UpdateStatus(new ServerStatus { ConfigCode = oldCode });
        Assert.AreNotEqual(oldCode, serverCommand.ConfigCode,
            "add an AccessPoint should lead to a new ConfigCode.");
        oldCode = serverCommand.ConfigCode;

        //-----------
        // check
        //-----------
        accessPoint = await testInit.NewAccessPoint();
        await serverDom.Update(new ServerUpdateParams
        {
            AccessPoints = new PatchOfAccessPointOf { Value = new[] { accessPoint } }
        });
        var serverStatus = new ServerStatus { ConfigCode = oldCode };
        serverCommand = await testInit.AgentClient1.Server_UpdateStatus(serverStatus);
        Assert.AreNotEqual(oldCode, serverCommand.ConfigCode,
            "updating AccessPoint should lead to a new ConfigCode.");
        oldCode = serverCommand.ConfigCode;

        //-----------
        // check
        //-----------
        await serverDom.AgentClient.Server_Configure(await testInit.NewServerInfo());
        var serverModel = await testInit.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == serverDom.ServerId);
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
        await testInit.AgentClient1.Server_UpdateStatus(serverStatus);
        serverModel = await testInit.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == serverDom.ServerId);
        Assert.AreEqual(serverStatus.ConfigCode, serverModel.LastConfigCode.ToString(),
            "LastConfigCode should be changed even by incorrect ConfigCode");
        Assert.AreEqual(oldCode, serverModel.ConfigCode.ToString(),
            "ConfigCode should not be changed when there is no update");

        //-----------
        // check
        //-----------
        await testInit.AgentClient1.Server_UpdateStatus(new ServerStatus { ConfigCode = oldCode });
        serverModel = await testInit.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == serverDom.ServerId);
        Assert.AreEqual(serverModel.ConfigCode, serverModel.LastConfigCode,
            "LastConfigCode should be changed correct ConfigCode");

        //-----------
        // check Reconfig After Config finish
        //-----------
        accessPoint = await testInit.NewAccessPoint();
        await serverDom.Update(new ServerUpdateParams
        {
            AccessPoints = new PatchOfAccessPointOf { Value = new[] { accessPoint } }
        });
        var serverData = await testInit.ServersClient.GetAsync(testInit.ProjectId, serverDom.ServerId);
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

        testServers.Add(new TestServer(testInit, accessPointGroup.AccessPointGroupId, true, true, await testInit.NewEndPointIp6()));
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
            var sessionRequestEx = testInit.CreateSessionRequestEx(accessToken, hostEndPoint: testServer.ServerEndPoint);
            var sessionResponseEx = await agentClient.Session_Create(sessionRequestEx);
            if (sessionResponseEx.ErrorCode == SessionErrorCode.RedirectHost)
            {
                Assert.IsNotNull(sessionResponseEx.RedirectHostEndPoint);
                sessionRequestEx.HostEndPoint = sessionResponseEx.RedirectHostEndPoint!;
                testServer = testServers.First(x => sessionResponseEx.RedirectHostEndPoint!.Equals(x.ServerEndPoint));
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
        var testInit = await TestInit.Create();
        var dnsName1 = $"{Guid.NewGuid()}.com";
        var certificate1 = await testInit.CertificatesClient.CreateAsync(testInit.ProjectId, new CertificateCreateParams { SubjectName = $"CN={dnsName1}" });
        var farm1 = await AccessPointGroupDom.Create(testInit, createParams: new AccessPointGroupCreateParams
        {
            CertificateId = certificate1.CertificateId
        });

        var dnsName2 = $"{Guid.NewGuid()}.com";
        var certificate2 = await testInit.CertificatesClient.CreateAsync(testInit.ProjectId, new CertificateCreateParams { SubjectName = $"CN={dnsName2}" });
        var farm2 = await AccessPointGroupDom.Create(testInit, createParams: new AccessPointGroupCreateParams
        {
            CertificateId = certificate1.CertificateId
        });


        //-----------
        // check: get certificate by publicIp
        //-----------
        var certBuffer = await farm1.DefaultServer.AgentClient.GetSslCertificateData(new IPEndPoint(farm1.DefaultServer.ServerInfo.PublicIpAddresses.First(), 443));
        var certificate = new X509Certificate2(certBuffer);
        Assert.AreEqual(dnsName1, certificate.GetNameInfo(X509NameType.DnsName, false));

        //-----------
        // check: get certificate by privateIp
        //-----------
        certBuffer = await farm2.DefaultServer.AgentClient.GetSslCertificateData(new IPEndPoint(farm2.DefaultServer.ServerInfo.PublicIpAddresses.First(), 443));
        certificate = new X509Certificate2(certBuffer);
        Assert.AreEqual(dnsName2, certificate.GetNameInfo(X509NameType.DnsName, false));

        //-----------
        // check: check not found
        //-----------
        try
        {
            await farm1.DefaultServer.AgentClient.GetSslCertificateData(new IPEndPoint(farm2.DefaultServer.ServerInfo.PublicIpAddresses.First(), 443));
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