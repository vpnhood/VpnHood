using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VpnHood.Common.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.ServerUtils;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AgentServerTest
{
    // return the only PublicInToken AccessPoint
    private async Task<AccessPoint[]> Configure_auto_update_accessPoints_on_internal(ServerDom serverDom)
    {
        var testInit = serverDom.TestInit;
        // create serverInfo
        var publicIp = await testInit.NewIpV6();
        var privateIp = await testInit.NewIpV4();
        serverDom.ServerInfo.PrivateIpAddresses = new[] { publicIp, privateIp, await testInit.NewIpV6(), privateIp };
        serverDom.ServerInfo.PublicIpAddresses = new[] { publicIp, await testInit.NewIpV4(), await testInit.NewIpV6() };

        //Configure
        await serverDom.Configure();
        Assert.AreEqual(testInit.AgentOptions.ServerUpdateStatusInterval, serverDom.ServerConfig.UpdateStatusInterval);
        Assert.AreEqual(serverDom.ServerConfig.TcpEndPointsValue.Length, serverDom.ServerInfo.PrivateIpAddresses.Distinct().Count(),
            "Duplicate listener!");

        //-----------
        // check: Configure with AutoUpdate is true (ServerModel.ServerFarmId is set)
        //-----------
        await serverDom.Reload();
        var accessPoints = serverDom.Server.AccessPoints.ToArray();
        var serverInfo = serverDom.ServerInfo;
        var serverConfig = serverDom.ServerConfig;
        var totalServerInfoIpAddress = serverInfo.PrivateIpAddresses.Concat(serverInfo.PublicIpAddresses).Distinct().Count();
        Assert.AreEqual(totalServerInfoIpAddress, accessPoints.Length);

        // private[0]
        var accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PrivateIpAddresses.ToArray()[0].ToString());
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
        return accessPoints.Where(x => x.AccessPointMode == AccessPointMode.PublicInToken).ToArray();
    }

    [TestMethod]
    public async Task Configure_when_AutoConfigure_is_on()
    {
        // create serverInfo
        var farm = await ServerFarmDom.Create(serverCount: 0);
        var serverDom = await farm.AddNewServer();
        var publicInTokenAccessPoints1 = await Configure_auto_update_accessPoints_on_internal(serverDom);
        var publicInTokenAccessPoints2 = await Configure_auto_update_accessPoints_on_internal(serverDom);

        // --------
        // Check: The only PublicInToken should be changed by second configure
        // --------
        Assert.AreEqual(2, publicInTokenAccessPoints1.Length);
        Assert.AreEqual(2, publicInTokenAccessPoints2.Length);
        CollectionAssert.AreNotEqual(publicInTokenAccessPoints1, publicInTokenAccessPoints2);
        var accessPoints = serverDom.Server.AccessPoints.ToArray();
        
        Assert.AreEqual(publicInTokenAccessPoints2.Single(x => IPAddress.Parse(x.IpAddress).AddressFamily == AddressFamily.InterNetwork).IpAddress,
            accessPoints.Single(x => x.AccessPointMode == AccessPointMode.PublicInToken && IPAddress.Parse(x.IpAddress).AddressFamily == AddressFamily.InterNetwork).IpAddress,
            "public access point should have on IPv4");

        Assert.AreEqual(publicInTokenAccessPoints2.Single(x => IPAddress.Parse(x.IpAddress).AddressFamily == AddressFamily.InterNetworkV6).IpAddress,
            accessPoints.Single(x => x.AccessPointMode == AccessPointMode.PublicInToken && IPAddress.Parse(x.IpAddress).AddressFamily == AddressFamily.InterNetworkV6).IpAddress,
            "public access point should have on IPv6");


        // --------
        // Check: Keep last server tokenAccessPoint if publicIp is same
        // --------

        // create serverInfo
        serverDom.ServerInfo.PrivateIpAddresses = new[] { await farm.TestInit.NewIpV4(), await farm.TestInit.NewIpV6() };
        serverDom.ServerInfo.PublicIpAddresses = new[]
        {
            await farm.TestInit.NewIpV4(), await farm.TestInit.NewIpV6(), 
            IPAddress.Parse(publicInTokenAccessPoints2[0].IpAddress),
            IPAddress.Parse(publicInTokenAccessPoints2[1].IpAddress)
        };

        //Configure
        await serverDom.Configure();
        await serverDom.Reload();
        accessPoints = serverDom.Server.AccessPoints.ToArray();

        Assert.AreEqual(publicInTokenAccessPoints2.Single(x => IPAddress.Parse(x.IpAddress).AddressFamily == AddressFamily.InterNetwork).IpAddress,
            accessPoints.Single(x => x.AccessPointMode == AccessPointMode.PublicInToken && IPAddress.Parse(x.IpAddress).AddressFamily == AddressFamily.InterNetwork).IpAddress,
            "public access point should have on IPv4");
        
        Assert.AreEqual(publicInTokenAccessPoints2.Single(x => IPAddress.Parse(x.IpAddress).AddressFamily == AddressFamily.InterNetworkV6).IpAddress,
            accessPoints.Single(x => x.AccessPointMode == AccessPointMode.PublicInToken && IPAddress.Parse(x.IpAddress).AddressFamily == AddressFamily.InterNetworkV6).IpAddress,
            "public access point should have on IPv6");

        // --------
        // Check: another server with same group should not have any PublicInTokenAccess
        // --------
        serverDom = await farm.AddNewServer();
        var publicInTokenAccessPoint = await Configure_auto_update_accessPoints_on_internal(serverDom);
        Assert.AreEqual(0, publicInTokenAccessPoint.Length);

        // --------
        // Check: another server with different group should have one PublicInTokenAccess
        // --------
        var farm2 = await ServerFarmDom.Create(serverCount: 0);
        serverDom = await farm2.AddNewServer();
        publicInTokenAccessPoint = await Configure_auto_update_accessPoints_on_internal(serverDom);
        Assert.IsNotNull(publicInTokenAccessPoint);
    }

    [TestMethod]
    public async Task Update_Tracking()
    {
        var farm = await ServerFarmDom.Create();

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
        var farm = await ServerFarmDom.Create(serverCount: 0);
        var dateTime = DateTime.UtcNow.AddSeconds(-1);
        var serverDom = await farm.AddNewServer(false);

        // create serverInfo and configure
        var publicIp = await farm.TestInit.NewIpV6();
        var serverInfo = await farm.TestInit.NewServerInfo(randomStatus: true);
        serverDom.ServerInfo = serverInfo;
        serverInfo.PrivateIpAddresses = new[] { publicIp, (await farm.TestInit.NewIpV4()), (await farm.TestInit.NewIpV6()) };
        serverInfo.PublicIpAddresses = new[] { publicIp, (await farm.TestInit.NewIpV4()), (await farm.TestInit.NewIpV6()) };
        await serverDom.Configure(false);
        await serverDom.Reload();

        var server = serverDom.Server;
        var serverStatusEx = serverDom.Server.ServerStatus;
        Assert.IsNotNull(serverStatusEx);

        Assert.AreEqual(serverInfo.Version.ToString(), server.Version);
        Assert.AreEqual(serverInfo.EnvironmentVersion.ToString(), server.EnvironmentVersion ?? "0.0.0");
        Assert.AreEqual(serverInfo.OsInfo, server.OsInfo);
        Assert.AreEqual(serverInfo.MachineName, server.MachineName);
        Assert.AreEqual(serverInfo.TotalMemory, server.TotalMemory);
        Assert.AreEqual(serverInfo.LogicalCoreCount, server.LogicalCoreCount);

        Assert.IsTrue(dateTime <= server.ConfigureTime);
        Assert.AreEqual(ServerState.Configuring, server.ServerState);
        Assert.AreEqual(serverInfo.Status.AvailableMemory, serverStatusEx.AvailableMemory);
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
        var serverStatus = TestInit.NewServerStatus(null, randomStatus: true);
        await serverDom.UpdateStatus(serverStatus);

        dateTime = DateTime.UtcNow;
        await Task.Delay(500);
        await serverDom.UpdateStatus(serverStatus);
        await farm.TestInit.Sync();
        await serverDom.UpdateStatus(serverStatus); // last status will not be synced
        await farm.TestInit.Sync();

        await serverDom.Reload();
        server = serverDom.Server;
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
        var farm = await ServerFarmDom.Create();
        var testInit = farm.TestInit;
        var serverDom = farm.DefaultServer;

        //-----------
        // check
        //-----------
        var oldCode = serverDom.ServerStatus.ConfigCode;
        var accessPoint = await testInit.NewAccessPoint();
        await serverDom.Update(new ServerUpdateParams
        {
            AccessPoints = new PatchOfAccessPointOf { Value = new[] { accessPoint } }
        });

        var serverCommand = await serverDom.SendStatus();
        Assert.AreNotEqual(oldCode, serverCommand.ConfigCode,
            "add an AccessPoint should lead to a new ConfigCode.");

        //-----------
        // check
        //-----------
        oldCode = serverCommand.ConfigCode;
        accessPoint = await testInit.NewAccessPoint();
        await serverDom.Update(new ServerUpdateParams
        {
            AccessPoints = new PatchOfAccessPointOf { Value = new[] { accessPoint } }
        });
        serverCommand = await serverDom.SendStatus();
        Assert.AreNotEqual(oldCode, serverCommand.ConfigCode,
            "updating AccessPoint should lead to a new ConfigCode.");

        //-----------
        // check
        //-----------
        oldCode = serverCommand.ConfigCode;
        serverDom.ServerInfo = await testInit.NewServerInfo(randomStatus: true);
        serverDom.ServerInfo.Status.ConfigCode = Guid.NewGuid().ToString();
        await serverDom.SendStatus(false);
        var serverModel = await testInit.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == serverDom.ServerId);
        Assert.AreEqual(serverDom.ServerInfo.Status.ConfigCode, serverModel.LastConfigCode.ToString(),
            "LastConfigCode should be set by Server_UpdateStatus.");

        Assert.AreEqual(oldCode, serverModel.ConfigCode.ToString(),
            "ConfigCode should not be changed by ConfigureServer.");

        Assert.AreNotEqual(serverModel.LastConfigCode, serverModel.ConfigCode,
            "LastConfigCode should be changed after UpdateStatus.");

        oldCode = serverCommand.ConfigCode;

        //-----------
        // check
        //-----------
        serverDom.ServerInfo.Status.ConfigCode = Guid.NewGuid().ToString();
        serverCommand = await serverDom.SendStatus(false);
        serverModel = await testInit.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == serverDom.ServerId);
        Assert.AreEqual(serverDom.ServerInfo.Status.ConfigCode, serverModel.LastConfigCode.ToString(),
            "LastConfigCode should be changed even by incorrect ConfigCode");
        Assert.AreEqual(oldCode, serverModel.ConfigCode.ToString(),
            "ConfigCode should not be changed when there is no update");

        //-----------
        // check
        //-----------
        serverDom.ServerInfo.Status.ConfigCode = serverCommand.ConfigCode;
        await serverDom.SendStatus(false);
        serverModel = await testInit.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == serverDom.ServerId);
        Assert.AreEqual(serverModel.ConfigCode, serverModel.LastConfigCode,
            "LastConfigCode should be changed to recent ConfigCode");

        //-----------
        // check Reconfig After Config finish
        //-----------
        await serverDom.Update(new ServerUpdateParams
        {
            AccessPoints = new PatchOfAccessPointOf { Value = new[] { await testInit.NewAccessPoint() } }
        });
        await serverDom.Reload();
        Assert.AreEqual(ServerState.Configuring, serverDom.Server.ServerState);
    }

    [TestMethod]
    public async Task Reconfig_by_changing_farm()
    {
        var farm1 = await ServerFarmDom.Create();
        var farm2 = await ServerFarmDom.Create(farm1.TestInit);

        var oldCode = farm1.DefaultServer.ServerInfo.Status.ConfigCode;
        await farm1.DefaultServer.Client.UpdateAsync(farm1.ProjectId, farm1.DefaultServer.ServerId,
            new ServerUpdateParams { ServerFarmId = new PatchOfGuid { Value = farm2.ServerFarmId } });

        var serverCommand = await farm1.DefaultServer.UpdateStatus(new ServerStatus { ConfigCode = oldCode });
        Assert.AreNotEqual(oldCode, serverCommand.ConfigCode,
            "Updating ServerFarmId should lead to a new ConfigCode");
    }

    [TestMethod]
    public async Task Configure_when_AutoConfigure_is_off()
    {
        // create serverInfo
        var farm = await ServerFarmDom.Create();
        var accessPoints = farm.DefaultServer.Server.AccessPoints.ToArray();
        await farm.DefaultServer.Update(new ServerUpdateParams
        {
            AutoConfigure = new PatchOfBoolean { Value = false }
        });

        farm.DefaultServer.ServerInfo.PrivateIpAddresses = new[] { await farm.TestInit.NewIpV4(), await farm.TestInit.NewIpV4(), await farm.TestInit.NewIpV6() };
        farm.DefaultServer.ServerInfo.PublicIpAddresses = new[] { await farm.TestInit.NewIpV6(), await farm.TestInit.NewIpV4(), await farm.TestInit.NewIpV6() };

        // Configure
        await farm.DefaultServer.Configure();
        await farm.DefaultServer.Reload();
        Assert.AreEqual(accessPoints.Length, farm.DefaultServer.Server.AccessPoints.Count);
    }

    [TestMethod]
    public async Task LoadBalancer()
    {
        var farm = await ServerFarmDom.Create(serverCount: 0);
        farm.TestInit.AgentOptions.AllowRedirect = true;

        // Create and init servers
        var serverDom1 = await farm.AddNewServer();
        var serverDom2 = await farm.AddNewServer();
        var serverDom3 = await farm.AddNewServer(sendStatus: false);
        var serverDom4 = await farm.AddNewServer(configure: false);
        var serverDom5 = await farm.AddNewServer(sendStatus: false);
        var serverDom6 = await farm.AddNewServer();

        // configure serverDom5 with ipv6
        serverDom5.ServerInfo.PublicIpAddresses = new[] { await serverDom5.TestInit.NewIpV6(), await serverDom5.TestInit.NewIpV6() };
        serverDom5.ServerInfo.PrivateIpAddresses = serverDom5.ServerInfo.PublicIpAddresses;
        await serverDom5.Configure();

        // make sure all accessPoints are initialized
        await farm.ReloadServers();

        // create access token
        var accessTokenDom = await farm.CreateAccessToken();

        // create sessions
        for (var i = 0; i < 10; i++)
        {
            var addressFamily = i == 9  ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork; //only one IPv6 request
            var sessionDom = await accessTokenDom.CreateSession(addressFamily: addressFamily, autoRedirect: true);
            
            // find the server that create the session
            var serverDom = farm.FindServerByEndPoint(sessionDom.SessionRequestEx.HostEndPoint);
            serverDom.ServerInfo.Status.SessionCount++;
            await serverDom.SendStatus();
        }

        // some server should not be selected
        Assert.AreEqual(0, serverDom3.ServerStatus.SessionCount, "Should not use server in Configuring state.");
        Assert.AreEqual(0, serverDom4.ServerStatus.SessionCount, "Should not use server in Configuring state.");
        Assert.AreEqual(1, serverDom5.ServerStatus.SessionCount, "IpVersion was not respected.");

        // each server sessions must be 3
        Assert.AreEqual(3, serverDom1.ServerStatus.SessionCount);
        Assert.AreEqual(3, serverDom2.ServerStatus.SessionCount);
        Assert.AreEqual(3, serverDom6.ServerStatus.SessionCount);
    }

    [TestMethod]
    public async Task Fail_Configure_by_old_version()
    {
        var farm = await ServerFarmDom.Create();
        farm.DefaultServer.ServerInfo.Version = Version.Parse("0.0.1");

        //Configure
        try
        {
            await farm.DefaultServer.Configure();
            Assert.Fail($"{nameof(NotSupportedException)} was expected.");
        }
        catch (ApiException e)
        {
            Assert.AreEqual(nameof(NotSupportedException), e.ExceptionTypeName);

            await farm.DefaultServer.Reload();
            Assert.IsTrue(farm.DefaultServer.Server.LastConfigError?.Contains("version", StringComparison.OrdinalIgnoreCase));
        }

        // LastConfigError must be removed after successful configuration
        farm.DefaultServer.ServerInfo.Version = ServerUtil.MinServerVersion;
        await farm.DefaultServer.Configure();
        await farm.DefaultServer.Reload();
        Assert.IsNull(farm.DefaultServer.Server.LastConfigError);
    }

    [TestMethod]
    public async Task Server_AutoConfigMemory()
    {
        const long gb = 0x40000000;

        var sampler = await ServerFarmDom.Create(serverCount: 0);
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
        var sampler = await ServerFarmDom.Create(serverCount: 1);
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
        var farm1 = await ServerFarmDom.Create(testInit, createParams: new ServerFarmCreateParams
        {
            CertificateId = certificate1.CertificateId
        });

        var dnsName2 = $"{Guid.NewGuid()}.com";
        var certificate2 = await testInit.CertificatesClient.CreateAsync(testInit.ProjectId, new CertificateCreateParams { SubjectName = $"CN={dnsName2}" });
        var farm2 = await ServerFarmDom.Create(testInit, createParams: new ServerFarmCreateParams
        {
            CertificateId = certificate2.CertificateId
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
    }

    [TestMethod]
    public async Task Server_UpdateStatus()
    {
        var farm = await ServerFarmDom.Create(serverCount: 0);
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