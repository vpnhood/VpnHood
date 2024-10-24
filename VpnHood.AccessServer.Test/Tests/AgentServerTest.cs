using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Agent;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Server.Access;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class AgentServerTest
{
    // return the only PublicInToken AccessPoint
    private async Task<AccessPoint[]> Configure_auto_update_accessPoints_on_internal(ServerDom serverDom)
    {
        var testApp = serverDom.TestApp;

        // create serverInfo
        var publicIp = testApp.NewIpV6();
        var privateIp = testApp.NewIpV4();
        var ipAddresses = new[] { publicIp, privateIp, testApp.NewIpV6(), privateIp };
        serverDom.ServerInfo.PrivateIpAddresses = ipAddresses;
        serverDom.ServerInfo.PublicIpAddresses = [publicIp, testApp.NewIpV4(), testApp.NewIpV6()];

        //Configure
        await serverDom.Configure();
        Assert.AreEqual(testApp.AgentTestApp.AgentOptions.ServerUpdateStatusInterval,
            serverDom.ServerConfig.UpdateStatusInterval);
        Assert.AreEqual(serverDom.ServerConfig.TcpEndPointsValue.Length,
            serverDom.ServerInfo.PrivateIpAddresses.Distinct().Count(),
            "Duplicate listener!");

        //-----------
        // check: Configure with AutoUpdate is true (ServerReportModel.ServerFarmId is set)
        //-----------
        await serverDom.Reload();
        var accessPoints = serverDom.Server.AccessPoints.ToArray();
        var serverInfo = serverDom.ServerInfo;
        var serverConfig = serverDom.ServerConfig;
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
        Assert.IsTrue(accessPoint.UdpPort > 0);
        Assert.IsTrue(accessPoint.IsListen, "shared publicIp and privateIp");
        Assert.IsTrue(serverConfig.TcpEndPointsValue.Any(x => x.ToString() == accessEndPoint.ToString()));

        // private[1]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PrivateIpAddresses.ToArray()[1].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.AreEqual(AccessPointMode.Private, accessPoint.AccessPointMode);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.UdpPort > 0);
        Assert.IsTrue(accessPoint.IsListen);
        Assert.IsTrue(serverConfig.TcpEndPointsValue.Any(x => x.ToString() == accessEndPoint.ToString()));

        // private[2]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PrivateIpAddresses.ToArray()[2].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.AreEqual(AccessPointMode.Private, accessPoint.AccessPointMode);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.UdpPort > 0);
        Assert.IsTrue(accessPoint.IsListen);
        Assert.IsTrue(serverConfig.TcpEndPointsValue.Any(x => x.ToString() == accessEndPoint.ToString()));

        // public[0]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PublicIpAddresses.ToArray()[0].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.UdpPort > 0);
        Assert.IsTrue(accessPoint.IsListen, "shared publicIp and privateIp");
        Assert.IsTrue(serverConfig.TcpEndPointsValue.Any(x => x.ToString() == accessEndPoint.ToString()));

        // public[1]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PublicIpAddresses.ToArray()[1].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.UdpPort > 0);
        Assert.IsFalse(accessPoint.IsListen);
        Assert.IsFalse(serverConfig.TcpEndPointsValue.Any(x => x.ToString() == accessEndPoint.ToString()));

        // public[2]
        accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PublicIpAddresses.ToArray()[2].ToString());
        accessEndPoint = new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken);
        Assert.AreEqual(443, accessPoint.TcpPort);
        Assert.IsTrue(accessPoint.UdpPort > 0);
        Assert.IsFalse(accessPoint.IsListen);
        Assert.IsFalse(serverConfig.TcpEndPointsValue.Any(x => x.ToString() == accessEndPoint.ToString()));

        // PublicInToken should never be deleted
        return accessPoints.Where(x => x.AccessPointMode == AccessPointMode.PublicInToken).ToArray();
    }

    [TestMethod]
    public async Task Configure_when_AutoConfigure_is_on()
    {
        // create serverInfo
        using var farm = await ServerFarmDom.Create(serverCount: 0);
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

        Assert.AreEqual(
            publicInTokenAccessPoints2
                .Single(x => IPAddress.Parse(x.IpAddress).AddressFamily == AddressFamily.InterNetwork).IpAddress,
            accessPoints.Single(x =>
                x.AccessPointMode == AccessPointMode.PublicInToken &&
                IPAddress.Parse(x.IpAddress).AddressFamily == AddressFamily.InterNetwork).IpAddress,
            "public access point should have on IPv4");

        Assert.AreEqual(
            publicInTokenAccessPoints2
                .Single(x => IPAddress.Parse(x.IpAddress).AddressFamily == AddressFamily.InterNetworkV6).IpAddress,
            accessPoints.Single(x =>
                x.AccessPointMode == AccessPointMode.PublicInToken &&
                IPAddress.Parse(x.IpAddress).AddressFamily == AddressFamily.InterNetworkV6).IpAddress,
            "public access point should have on IPv6");


        // --------
        // Check: Keep last server tokenAccessPoint if publicIp is same
        // --------

        // create serverInfo
        serverDom.ServerInfo.PrivateIpAddresses = [farm.TestApp.NewIpV4(), farm.TestApp.NewIpV6()];
        serverDom.ServerInfo.PublicIpAddresses = [
            farm.TestApp.NewIpV4(),
            farm.TestApp.NewIpV6(),
            IPAddress.Parse(publicInTokenAccessPoints2[0].IpAddress),
            IPAddress.Parse(publicInTokenAccessPoints2[1].IpAddress)
        ];

        //Configure
        await serverDom.Configure();
        await serverDom.Reload();
        accessPoints = serverDom.Server.AccessPoints.ToArray();

        Assert.AreEqual(
            publicInTokenAccessPoints2
                .Single(x => IPAddress.Parse(x.IpAddress).AddressFamily == AddressFamily.InterNetwork).IpAddress,
            accessPoints.Single(x =>
                x.AccessPointMode == AccessPointMode.PublicInToken &&
                IPAddress.Parse(x.IpAddress).AddressFamily == AddressFamily.InterNetwork).IpAddress,
            "public access point should have on IPv4");

        Assert.AreEqual(
            publicInTokenAccessPoints2
                .Single(x => IPAddress.Parse(x.IpAddress).AddressFamily == AddressFamily.InterNetworkV6).IpAddress,
            accessPoints.Single(x =>
                x.AccessPointMode == AccessPointMode.PublicInToken &&
                IPAddress.Parse(x.IpAddress).AddressFamily == AddressFamily.InterNetworkV6).IpAddress,
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
    public async Task Configure_manual_UDP_return_nothing_when_port_is_minus_one()
    {
        // create serverInfo
        using var farm = await ServerFarmDom.Create();
        await farm.DefaultServer.Update(new ServerUpdateParams {
            AutoConfigure = new PatchOfBoolean { Value = false },
            AccessPoints = new PatchOfAccessPointOf { Value = [farm.TestApp.NewAccessPoint(udpPort: -1)] }
        });

        // Configure
        await farm.DefaultServer.Configure();
        await farm.DefaultServer.Reload();
        Assert.AreEqual(0, farm.DefaultServer.ServerConfig.UdpEndPoints?.Length);
    }

    [TestMethod]
    public async Task Configure_should_update_farm_server_token_and_update_cache()
    {
        // create serverInfo
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        var serverDom = await farm.AddNewServer();

        // create a session to make sure agent cache the objects
        var accessToken = await farm.CreateAccessToken();
        await accessToken.CreateSession();

        // reconfig server by new endpoints
        serverDom.ServerInfo.PrivateIpAddresses = [farm.TestApp.NewIpV4(), farm.TestApp.NewIpV6()];
        serverDom.ServerInfo.PublicIpAddresses = [farm.TestApp.NewIpV4()];
        await serverDom.Configure();

        // create new session to see if agent cache has been updated
        var sessionDom = await accessToken.CreateSession();
        var token = Common.Tokens.Token.FromAccessKey(sessionDom.SessionResponseEx.AccessKey!);
        Assert.IsNotNull(token.ServerToken.HostEndPoints);
        Assert.AreEqual(
            token.ServerToken.HostEndPoints.First(x => x.Address.IsV4()).Address,
            serverDom.ServerInfo.PublicIpAddresses.First());
    }


    [TestMethod]
    public async Task Configure_UDP_for_first_time_only()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        var serverDom = await farm.AddNewServer(configure: false);

        // create serverInfo and configure
        var publicIp = farm.TestApp.NewIpV6();
        var serverInfo = farm.TestApp.NewServerInfo(randomStatus: true);
        var freeUdpPortV4 = serverInfo.FreeUdpPortV4;
        var freeUdpPortV6 = serverInfo.FreeUdpPortV6;
        serverDom.ServerInfo = serverInfo;
        serverInfo.PrivateIpAddresses = [publicIp, farm.TestApp.NewIpV4(), farm.TestApp.NewIpV6()];
        serverInfo.PublicIpAddresses = [publicIp, farm.TestApp.NewIpV4(), farm.TestApp.NewIpV6()];
        await serverDom.Configure();
        await serverDom.Reload();
        Assert.IsNotNull(serverDom.ServerConfig.UdpEndPoints);
        Assert.IsTrue(serverDom.ServerConfig.UdpEndPoints.Any(x =>
            x.AddressFamily == AddressFamily.InterNetwork && x.Port == freeUdpPortV4));
        Assert.IsTrue(serverDom.ServerConfig.UdpEndPoints.Any(x =>
            x.AddressFamily == AddressFamily.InterNetworkV6 && x.Port == freeUdpPortV6));

        // create new serverInfo and configure
        serverInfo.FreeUdpPortV4 = new Random().Next(20000, 30000);
        serverInfo.FreeUdpPortV6 = new Random().Next(20000, 30000);
        await serverDom.Configure();
        await serverDom.Reload();
        Assert.IsNotNull(serverDom.ServerConfig.UdpEndPoints);
        Assert.IsTrue(serverDom.ServerConfig.UdpEndPoints.Any(x =>
            x.AddressFamily == AddressFamily.InterNetwork && x.Port == freeUdpPortV4));
        Assert.IsTrue(serverDom.ServerConfig.UdpEndPoints.Any(x =>
            x.AddressFamily == AddressFamily.InterNetworkV6 && x.Port == freeUdpPortV6));
    }


    [TestMethod]
    public async Task Configure()
    {
        var farmCreateParams = new ServerFarmCreateParams();
        using var farm = await ServerFarmDom.Create(serverCount: 0, createParams: farmCreateParams);
        var dateTime = DateTime.UtcNow.AddSeconds(-1);
        var serverDom = await farm.AddNewServer(configure: false);

        // create serverInfo and configure
        var publicIp = farm.TestApp.NewIpV6();
        var serverInfo = farm.TestApp.NewServerInfo(randomStatus: true);
        serverDom.ServerInfo = serverInfo;
        serverInfo.PrivateIpAddresses = [publicIp, (farm.TestApp.NewIpV4())];
        serverInfo.PublicIpAddresses = [publicIp, (farm.TestApp.NewIpV4())];
        await serverDom.Configure(false);
        await serverDom.Reload();

        // check configuration
        CollectionAssert.AreEqual(farm.ServerFarm.Secret, serverDom.ServerConfig.ServerSecret);

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
        Assert.AreEqual(serverInfo.Status.TunnelSpeed.Sent, serverStatusEx.TunnelSendSpeed);
        Assert.AreEqual(serverInfo.Status.TunnelSpeed.Received, serverStatusEx.TunnelReceiveSpeed);
        Assert.IsTrue(dateTime <= serverStatusEx.CreatedTime);

        //-----------
        // check: Check ServerStatus log is inserted
        //-----------
        var serverStatus = TestApp.NewServerStatus(null, randomStatus: true);
        await serverDom.SendStatus(serverStatus);

        dateTime = DateTime.UtcNow;
        await Task.Delay(500);
        await serverDom.SendStatus(serverStatus);
        await farm.TestApp.Sync();
        await serverDom.SendStatus(serverStatus); // last status will not be synced
        await farm.TestApp.Sync();

        await serverDom.Reload();
        server = serverDom.Server;
        Assert.AreEqual(serverStatus.AvailableMemory, server.ServerStatus?.AvailableMemory);
        Assert.AreNotEqual(ServerState.Configuring, server.ServerState);
        Assert.AreEqual(serverStatus.TcpConnectionCount, server.ServerStatus?.TcpConnectionCount);
        Assert.AreEqual(serverStatus.UdpConnectionCount, server.ServerStatus?.UdpConnectionCount);
        Assert.AreEqual(serverStatus.SessionCount, server.ServerStatus?.SessionCount);
        Assert.AreEqual(serverStatus.ThreadCount, server.ServerStatus?.ThreadCount);
        Assert.IsTrue(server.ServerStatus?.CreatedTime > dateTime);
        Assert.IsTrue(serverInfo.PublicIpAddresses.Contains(IPAddress.Parse(server.PublicIpV4!)));
        Assert.IsTrue(serverInfo.PublicIpAddresses.Contains(IPAddress.Parse(server.PublicIpV6!)));
        Assert.AreEqual(serverInfo.Status.TotalSwapMemory / VhUtil.Megabytes, server.TotalSwapMemoryMb);
    }

    [TestMethod]
    public async Task Configure_settings()
    {
        using var farm = await ServerFarmDom.Create();
        await farm.DefaultServer.Update(new ServerUpdateParams {
            ConfigSwapMemorySizeMb = new PatchOfNullableInteger{Value = 1100}
        });

        // Configure
        await farm.DefaultServer.Configure();

        // Make sure the server SwapMemoryMb has been returned
        Assert.AreEqual(1100, farm.DefaultServer.ServerConfig.SwapMemorySizeMb);
    }


    [TestMethod]
    public async Task AutoConfig_should_not_remove_access_point_by_empty_address_family()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        var testApp = farm.TestApp;
        var serverDom = await farm.AddNewServer(configure: false, sendStatus: false);

        // create serverInfo
        var privateIpV4 = testApp.NewIpV4();
        var privateIpV6 = testApp.NewIpV6();
        var publicIpV4 = testApp.NewIpV4();
        var publicIpV6 = testApp.NewIpV6();
        serverDom.ServerInfo.PrivateIpAddresses = [privateIpV4, privateIpV6];
        serverDom.ServerInfo.PublicIpAddresses = [publicIpV4, publicIpV6];

        //Configure
        await serverDom.Configure();

        // make sure new IP has been configured
        await serverDom.Reload();
        Assert.IsTrue(serverDom.Server.AccessPoints.Any(
            x => x.AccessPointMode == AccessPointMode.PublicInToken && x.IpAddress == publicIpV4.ToString() &&
                 !x.IsListen));
        Assert.IsTrue(serverDom.Server.AccessPoints.Any(
            x => x.AccessPointMode == AccessPointMode.PublicInToken && x.IpAddress == publicIpV6.ToString() &&
                 !x.IsListen));
        Assert.IsTrue(serverDom.Server.AccessPoints.Any(
            x => x.AccessPointMode == AccessPointMode.Private && x.IpAddress == privateIpV4.ToString() && x.IsListen));
        Assert.IsTrue(serverDom.Server.AccessPoints.Any(
            x => x.AccessPointMode == AccessPointMode.Private && x.IpAddress == privateIpV6.ToString() && x.IsListen));

        // --------
        // Check: remove publicIpV6
        // --------
        serverDom.ServerInfo.PublicIpAddresses = [publicIpV4];
        await serverDom.Configure();
        await serverDom.Reload();

        Assert.IsTrue(serverDom.Server.AccessPoints.Any(
            x => x.AccessPointMode == AccessPointMode.PublicInToken && x.IpAddress == publicIpV4.ToString() &&
                 !x.IsListen));
        Assert.IsTrue(serverDom.Server.AccessPoints.Any(
            x => x.AccessPointMode == AccessPointMode.PublicInToken && x.IpAddress == publicIpV6.ToString() &&
                 !x.IsListen));
        Assert.IsTrue(serverDom.Server.AccessPoints.Any(
            x => x.AccessPointMode == AccessPointMode.Private && x.IpAddress == privateIpV4.ToString() && x.IsListen));
        Assert.IsTrue(serverDom.Server.AccessPoints.Any(
            x => x.AccessPointMode == AccessPointMode.Private && x.IpAddress == privateIpV6.ToString() && x.IsListen));

        // --------
        // Check: remove publicIpV4
        // --------
        serverDom.ServerInfo.PublicIpAddresses = [publicIpV6];
        await serverDom.Configure();
        await serverDom.Reload();

        Assert.IsTrue(serverDom.Server.AccessPoints.Any(
            x => x.AccessPointMode == AccessPointMode.PublicInToken && x.IpAddress == publicIpV4.ToString() &&
                 !x.IsListen));
        Assert.IsTrue(serverDom.Server.AccessPoints.Any(
            x => x.AccessPointMode == AccessPointMode.PublicInToken && x.IpAddress == publicIpV6.ToString() &&
                 !x.IsListen));
        Assert.IsTrue(serverDom.Server.AccessPoints.Any(
            x => x.AccessPointMode == AccessPointMode.Private && x.IpAddress == privateIpV4.ToString() && x.IsListen));
        Assert.IsTrue(serverDom.Server.AccessPoints.Any(
            x => x.AccessPointMode == AccessPointMode.Private && x.IpAddress == privateIpV6.ToString() && x.IsListen));
    }


    [TestMethod]
    public async Task Reconfig()
    {
        using var farm = await ServerFarmDom.Create();
        var testApp = farm.TestApp;
        var serverDom = farm.DefaultServer;

        //-----------
        // check
        //-----------
        var oldCode = serverDom.ServerStatus.ConfigCode;
        var accessPoint = testApp.NewAccessPoint();
        await serverDom.Update(new ServerUpdateParams {
            AccessPoints = new PatchOfAccessPointOf { Value = [accessPoint] }
        });

        var serverCommand = await serverDom.SendStatus();
        Assert.AreNotEqual(oldCode, serverCommand.ConfigCode,
            "add an AccessPoint should lead to a new ConfigCode.");

        //-----------
        // check
        //-----------
        oldCode = serverCommand.ConfigCode;
        accessPoint = testApp.NewAccessPoint();
        await serverDom.Update(new ServerUpdateParams {
            AccessPoints = new PatchOfAccessPointOf { Value = [accessPoint] }
        });
        serverCommand = await serverDom.SendStatus();
        Assert.AreNotEqual(oldCode, serverCommand.ConfigCode,
            "updating AccessPoint should lead to a new ConfigCode.");

        //-----------
        // check
        //-----------
        oldCode = serverCommand.ConfigCode;
        serverDom.ServerInfo = testApp.NewServerInfo(randomStatus: true);
        serverDom.ServerInfo.Status.ConfigCode = Guid.NewGuid().ToString();
        await serverDom.SendStatus(false);
        var serverModel = await testApp.VhContext.Servers.AsNoTracking()
            .SingleAsync(x => x.ServerId == serverDom.ServerId);
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
        serverModel = await testApp.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == serverDom.ServerId);
        Assert.AreEqual(serverDom.ServerInfo.Status.ConfigCode, serverModel.LastConfigCode.ToString(),
            "LastConfigCode should be changed even by incorrect ConfigCode");
        Assert.AreEqual(oldCode, serverModel.ConfigCode.ToString(),
            "ConfigCode should not be changed when there is no update");

        //-----------
        // check
        //-----------
        serverDom.ServerInfo.Status.ConfigCode = serverCommand.ConfigCode;
        await serverDom.SendStatus(false);
        serverModel = await testApp.VhContext.Servers.AsNoTracking().SingleAsync(x => x.ServerId == serverDom.ServerId);
        Assert.AreEqual(serverModel.ConfigCode, serverModel.LastConfigCode,
            "LastConfigCode should be changed to recent ConfigCode");

        //-----------
        // check Reconfig After Config finish
        //-----------
        await serverDom.Update(new ServerUpdateParams {
            AccessPoints = new PatchOfAccessPointOf { Value = [testApp.NewAccessPoint()] }
        });
        await serverDom.Reload();
        Assert.AreEqual(ServerState.Configuring, serverDom.Server.ServerState);
    }

    [TestMethod]
    public async Task Reconfig_by_changing_farm()
    {
        var farm1 = await ServerFarmDom.Create();
        var farm2 = await ServerFarmDom.Create(farm1.TestApp);

        var oldCode = farm1.DefaultServer.ServerInfo.Status.ConfigCode;
        await farm1.DefaultServer.Client.UpdateAsync(farm1.ProjectId, farm1.DefaultServer.ServerId,
            new ServerUpdateParams { ServerFarmId = new PatchOfGuid { Value = farm2.ServerFarmId } });

        var serverCommand = await farm1.DefaultServer.SendStatus(new ServerStatus { ConfigCode = oldCode });
        Assert.AreNotEqual(oldCode, serverCommand.ConfigCode,
            "Updating ServerFarmId should lead to a new ConfigCode");
    }

    [TestMethod]
    public async Task Configure_when_AutoConfigure_is_off()
    {
        // create serverInfo
        using var farm = await ServerFarmDom.Create();
        var accessPoints = farm.DefaultServer.Server.AccessPoints.ToArray();
        await farm.DefaultServer.Update(new ServerUpdateParams {
            AutoConfigure = new PatchOfBoolean { Value = false }
        });

        farm.DefaultServer.ServerInfo.PrivateIpAddresses = [
            farm.TestApp.NewIpV4(), farm.TestApp.NewIpV4(), farm.TestApp.NewIpV6()
        ];
        farm.DefaultServer.ServerInfo.PublicIpAddresses = [
            farm.TestApp.NewIpV6(), farm.TestApp.NewIpV4(), farm.TestApp.NewIpV6()
        ];

        // Configure
        await farm.DefaultServer.Configure();
        await farm.DefaultServer.Reload();
        Assert.AreEqual(accessPoints.Length, farm.DefaultServer.Server.AccessPoints.Count);
    }

    [TestMethod]
    public async Task Fail_Configure_by_old_version()
    {
        using var farm = await ServerFarmDom.Create();
        farm.DefaultServer.ServerInfo.Version = Version.Parse("0.0.1");

        //Configure
        await VhTestUtil.AssertApiException<NotSupportedException>(farm.DefaultServer.Configure());
        await farm.DefaultServer.Reload();
        Assert.IsTrue(
            farm.DefaultServer.Server.LastConfigError?.Contains("version", StringComparison.OrdinalIgnoreCase));

        // LastConfigError must be removed after successful configuration
        farm.DefaultServer.ServerInfo.Version = AgentOptions.MinServerVersion;
        await farm.DefaultServer.Configure();
        await farm.DefaultServer.Reload();
        Assert.IsNull(farm.DefaultServer.Server.LastConfigError);
    }

    [TestMethod]
    public async Task Server_AutoConfigMemory()
    {
        const long gb = 0x40000000;

        var sampler = await ServerFarmDom.Create(serverCount: 0);
        var sampleServer = await sampler.AddNewServer(configure: false);

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
        await sampler.TestApp.FlushCache();
        await sampler.TestApp.AgentCacheClient.InvalidateProject(sampler.ProjectId);

        // update status again
        await server.SendStatus(server.ServerInfo.Status);
        var servers = await sampler.TestApp.AgentCacheClient.GetServers(sampler.ProjectId);
        Assert.IsTrue(servers.Any(x => x.ServerId == server.ServerId));
    }

    [TestMethod]
    public async Task GetCertificateData()
    {
        using var testApp = await TestApp.Create();
        var dnsName1 = $"{Guid.NewGuid()}.com";
        var farm1 = await ServerFarmDom.Create(testApp);
        await farm1.CertificateReplace(new CertificateCreateParams {
            CertificateSigningRequest = new CertificateSigningRequest { CommonName = dnsName1 }
        });

        var dnsName2 = $"{Guid.NewGuid()}.com";
        var farm2 = await ServerFarmDom.Create(testApp);
        await farm2.CertificateReplace(new CertificateCreateParams {
            CertificateSigningRequest = new CertificateSigningRequest { CommonName = dnsName2 }
        });

        //-----------
        // check: get certificate by publicIp
        //-----------
        await farm1.DefaultServer.Configure();
        var certBuffer = farm1.DefaultServer.ServerConfig.Certificates.First().RawData;
        var certificate = new X509Certificate2(certBuffer);
        Assert.AreEqual(dnsName1, certificate.GetNameInfo(X509NameType.DnsName, false));

        //-----------
        // check: get certificate by privateIp
        //-----------
        await farm2.DefaultServer.Configure();
        certBuffer = farm2.DefaultServer.ServerConfig.Certificates.First().RawData;
        certificate = new X509Certificate2(certBuffer);
        Assert.AreEqual(dnsName2, certificate.GetNameInfo(X509NameType.DnsName, false));
    }

    [TestMethod]
    public async Task Reconfig_all_servers_after_farm_certificate_changed()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        var server1 = await farm.AddNewServer();
        var server2 = await farm.AddNewServer();

        await farm.CertificateReplace();

        var command1 = await server1.SendStatus();
        var command2 = await server2.SendStatus();
        Assert.AreNotEqual(command1.ConfigCode, server1.ServerConfig.ConfigCode);
        Assert.AreNotEqual(command2.ConfigCode, server2.ServerConfig.ConfigCode);
    }

    [TestMethod]
    public async Task Server_UpdateStatus()
    {
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        var testApp = farm.TestApp;
        var serverDom1 = await farm.AddNewServer();
        var serverDom2 = await farm.AddNewServer();

        await serverDom1.SendStatus(new ServerStatus { SessionCount = 1 });
        await serverDom1.SendStatus(new ServerStatus { SessionCount = 2 });
        await serverDom1.SendStatus(new ServerStatus { SessionCount = 3 });
        await serverDom1.SendStatus(new ServerStatus { SessionCount = 4, AvailableMemory = 100, CpuUsage = 2 });
        await testApp.FlushCache();

        await serverDom1.SendStatus(new ServerStatus { SessionCount = 9 });
        await serverDom1.SendStatus(new ServerStatus { SessionCount = 10 });
        await serverDom2.SendStatus(new ServerStatus { SessionCount = 19 });
        await serverDom2.SendStatus(new ServerStatus { SessionCount = 20 });

        var serverData1 = await testApp.ServersClient.GetAsync(testApp.ProjectId, serverDom1.ServerId);
        Assert.AreEqual(serverData1.Server.ServerStatus?.SessionCount, 10);

        var serverData2 = await testApp.ServersClient.GetAsync(testApp.ProjectId, serverDom2.ServerId);
        Assert.AreEqual(serverData2.Server.ServerStatus?.SessionCount, 20);

        await testApp.FlushCache();

        // check saving cache
        var serverStatus = await testApp.VhContext.ServerStatuses
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