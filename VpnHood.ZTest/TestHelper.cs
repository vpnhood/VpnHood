using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client;
using VpnHood.Client.App;
using VpnHood.Client.Device;
using VpnHood.Client.Diagnosing;
using VpnHood.Common;
using VpnHood.Common.Converters;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Server;
using VpnHood.Server.Messaging;
using VpnHood.Server.Providers.FileAccessServerProvider;
using VpnHood.Test.Factory;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Test;

internal static class TestHelper
{
    public static readonly Uri TEST_HttpsUri1 = new("https://www.quad9.net/");
    public static readonly Uri TEST_HttpsUri2 = new("https://www.shell.com/");
    public static readonly IPEndPoint TEST_NsEndPoint1 = IPEndPoint.Parse("9.9.9.9:53");
    public static readonly IPEndPoint TEST_NsEndPoint2 = IPEndPoint.Parse("149.112.112.112:53");
    public static readonly IPAddress TEST_PingAddress1 = IPAddress.Parse("9.9.9.9");
    public static readonly IPAddress TEST_PingAddress2 = IPAddress.Parse("1.1.1.1");
    public static readonly IPEndPoint TEST_TcpEndPoint1 = IPEndPoint.Parse("17.253.144.11:443"); //apple
    public static readonly IPEndPoint TEST_TcpEndPoint2 = IPEndPoint.Parse("17.253.144.12:443");


    public static readonly IPEndPoint
        TEST_NtpEndPoint1 = IPEndPoint.Parse("132.163.96.1:123"); // https://tf.nist.gov/tf-cgi/servers.cgi

    public static readonly IPEndPoint
        TEST_NtpEndPoint2 = IPEndPoint.Parse("132.163.96.2:123"); // https://tf.nist.gov/tf-cgi/servers.cgi

    public static readonly Uri TEST_InvalidUri = new("https://DBBC5764-D452-468F-8301-4B315507318F.zz");
    public static readonly IPAddress TEST_InvalidIp = IPAddress.Parse("192.168.199.199");
    public static readonly IPEndPoint TEST_InvalidEp = IPEndPointConverter.Parse("192.168.199.199:9999");

    private static int _accessItemIndex;

    public static string WorkingPath { get; } = Path.Combine(Path.GetTempPath(), "_test_vpnhood");

    public static string CreateNewFolder(string namePart)
    {
        var folder = Path.Combine(WorkingPath, $"{namePart}_{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    internal static void Cleanup()
    {
        try
        {
            if (Directory.Exists(WorkingPath))
                Directory.Delete(WorkingPath, true);
        }
        catch
        {
            // ignored
        }
    }

    public static void WaitForClientState(VpnHoodApp app, AppConnectionState connectionSate, int timeout = 5000)
    {
        var waitTime = 200;
        for (var elapsed = 0; elapsed < timeout && app.State.ConnectionState != connectionSate; elapsed += waitTime)
            Thread.Sleep(waitTime);

        Assert.AreEqual(connectionSate, app.State.ConnectionState);
    }

    public static void WaitForClientState(VpnHoodClient client, ClientState clientState, int timeout = 6000)
    {
        var waitTime = 200;
        for (var elapsed = 0; elapsed < timeout && client.State != clientState; elapsed += waitTime)
            Thread.Sleep(waitTime);

        Assert.AreEqual(clientState, client.State);
    }

    private static PingReply SendPing(Ping? ping = null, IPAddress? ipAddress = null, int timeout = 3000)
    {
        using var pingT = new Ping();
        ping ??= pingT;
        var pingOptions = new PingOptions
        {
            Ttl = TestNetProtector.ServerPingTtl // set ttl to control by test adapter
        };

        var buffer = new byte[100];
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = (byte)i;
        return ping.Send(ipAddress ?? TEST_PingAddress1, timeout, buffer, pingOptions);
    }

    private static bool SendHttpGet(HttpClient? httpClient = default, Uri? uri = default, int timeout = 3000)
    {
        using var httpClientT = new HttpClient(new HttpClientHandler
        {
            CheckCertificateRevocationList = false,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        });

        httpClient ??= httpClientT;
        var task = httpClient.GetStringAsync(uri ?? TEST_HttpsUri1);
        if (!task.Wait(timeout))
            throw new TimeoutException("GetStringAsync timeout!");
        var result = task.Result;
        return result.Length > 100;
    }

    public static void Test_Ping(Ping? ping = default, IPAddress? ipAddress = default, int timeout = 3000)
    {
        var pingReply = SendPing(ping, ipAddress, timeout);
        //if (pingReply.Status == IPStatus.TimedOut)
        //  pingReply = SendPing(ping, ipAddress, timeout); //try again
        Assert.AreEqual(IPStatus.Success, pingReply.Status);
    }

    public static void Test_Dns(UdpClient? udpClient = null, IPEndPoint? nsEndPoint = default, int timeout = 3000)
    {
        var hostEntry = DiagnoseUtil
            .GetHostEntry("www.google.com", nsEndPoint ?? TEST_NsEndPoint1, udpClient, timeout).Result;
        Assert.IsNotNull(hostEntry);
        Assert.IsTrue(hostEntry.AddressList.Length > 0);
    }

    public static void Test_Udp(UdpClient? udpClient = null, IPEndPoint? ntpEndPoint = default, int timeout = 3000)
    {
        using var udpClientT = new UdpClient();
        udpClient ??= udpClientT;
        var oldReceiveTimeout = udpClient.Client.ReceiveTimeout;
        udpClient.Client.ReceiveTimeout = timeout;
        var time = GetNetworkTimeByNtp(udpClient, ntpEndPoint ?? TEST_NtpEndPoint1, 2);
        udpClient.Client.ReceiveTimeout = oldReceiveTimeout;
        Assert.IsTrue(time > DateTime.Now.AddMinutes(-10));
    }

    public static void Test_Https(HttpClient? httpClient = default, Uri? uri = default, int timeout = 3000, bool throwError = true)
    {
        if (!SendHttpGet(httpClient, uri, timeout) && throwError)
            throw new Exception("Https get doesn't work!");
    }

    public static IPAddress[] TestIpAddresses
    {
        get
        {
            var addresses = new List<IPAddress>();
            addresses.AddRange(Dns.GetHostAddresses(TEST_HttpsUri1.Host));
            addresses.AddRange(Dns.GetHostAddresses(TEST_HttpsUri2.Host));
            addresses.Add(TEST_NsEndPoint1.Address);
            addresses.Add(TEST_NsEndPoint2.Address);
            addresses.Add(TEST_NtpEndPoint1.Address);
            addresses.Add(TEST_NtpEndPoint2.Address);
            addresses.Add(TEST_PingAddress1);
            addresses.Add(TEST_PingAddress2);
            addresses.Add(TEST_TcpEndPoint1.Address);
            addresses.Add(TEST_TcpEndPoint2.Address);
            addresses.Add(TEST_InvalidIp);
            addresses.Add(new ClientOptions().TcpProxyLoopbackAddressIpV4);
            return addresses.ToArray();
        }
    }

    public static Token CreateAccessToken(FileAccessServer fileAccessServer, IPEndPoint[]? hostEndPoints = null,
        int maxClientCount = 1, int maxTrafficByteCount = 0, DateTime? expirationTime = null)
    {
        return fileAccessServer.AccessItem_Create(
            hostEndPoints ?? fileAccessServer.ServerConfig.TcpEndPoints,
            tokenName: $"Test Server {++_accessItemIndex}",
            maxClientCount: maxClientCount,
            maxTrafficByteCount: maxTrafficByteCount,
            expirationTime: expirationTime
        ).Token;
    }

    public static Token CreateAccessToken(VpnHoodServer server,
        int maxClientCount = 1, int maxTrafficByteCount = 0, DateTime? expirationTime = null)
    {
        var testAccessServer = (TestAccessServer)server.AccessServer;
        var fileAccessServer = (FileAccessServer)testAccessServer.BaseAccessServer;
        return CreateAccessToken(fileAccessServer, null, maxClientCount, maxTrafficByteCount, expirationTime);
    }

    public static FileAccessServer CreateFileAccessServer(FileAccessServerOptions? options = null, string? storagePath = null)
    {
        storagePath ??= Path.Combine(WorkingPath, $"AccessServer_{Guid.NewGuid()}");
        options ??= CreateFileAccessServerOptions();
        return new FileAccessServer(storagePath, options);
    }

    public static FileAccessServerOptions CreateFileAccessServerOptions()
    {
        var options = new FileAccessServerOptions
        {
            TcpEndPoints = new[] { Util.GetFreeEndPoint(IPAddress.Loopback) },
            SessionOptions =
            {
                SyncCacheSize = 50,
                IcmpTimeout = TimeSpan.FromMilliseconds(100)
            },
        };
        return options;
    }

    public static VpnHoodServer CreateServer(IAccessServer? accessServer = null, bool autoStart = true)
    {
        return CreateServer(accessServer, null, autoStart);
    }

    public static VpnHoodServer CreateServer(FileAccessServerOptions? options, bool autoStart = true)
    {
        return CreateServer(null, options, autoStart);
    }

    private static VpnHoodServer CreateServer(IAccessServer? accessServer, FileAccessServerOptions? fileAccessServerOptions, bool autoStart)
    {
        if (accessServer != null && fileAccessServerOptions != null)
            throw new InvalidOperationException($"Could not set both {nameof(accessServer)} and {nameof(fileAccessServerOptions)}.");

        var autoDisposeAccessServer = false;
        if (accessServer == null)
        {
            accessServer = new TestAccessServer(CreateFileAccessServer(fileAccessServerOptions));
            autoDisposeAccessServer = true;
        }

        // ser server options
        var serverOptions = new ServerOptions
        {
            SocketFactory = new TestSocketFactory(true),
            ConfigureInterval = TimeSpan.FromMilliseconds(100),
            //CheckMaintenanceInterval = TimeSpan.Zero,
            AutoDisposeAccessServer = autoDisposeAccessServer,
            StoragePath = WorkingPath,
            PublicIpDiscovery = false, //it slows down the running tests
        };

        // Create server
        var server = new VpnHoodServer(accessServer, serverOptions);
        if (autoStart)
        {
            server.Start().Wait();
            Assert.AreEqual(ServerState.Started, server.State);
        }

        return server;
    }

    public static IDevice CreateDevice(TestDeviceOptions? options = default)
    {
        return new TestDevice(options);
    }

    public static IPacketCapture CreatePacketCapture(TestDeviceOptions? options = default)
    {
        return CreateDevice(options).CreatePacketCapture().Result;
    }

    public static VpnHoodClient CreateClient(Token token,
        IPacketCapture? packetCapture = default,
        TestDeviceOptions? deviceOptions = default,
        Guid? clientId = default,
        bool autoConnect = true,
        ClientOptions? options = default)
    {
        packetCapture ??= CreatePacketCapture(deviceOptions);
        clientId ??= Guid.NewGuid();
        options ??= new ClientOptions { MaxDatagramChannelCount = 1 };
        if (options.TcpTimeout == new ClientOptions().TcpTimeout) options.TcpTimeout = TimeSpan.FromSeconds(3);
        options.SocketFactory = new TestSocketFactory(false);
        options.PacketCaptureIncludeIpRanges = TestIpAddresses.Select(x => new IpRange(x)).ToArray();
        options.ExcludeLocalNetwork = false;

        var client = new VpnHoodClient(
            packetCapture,
            clientId.Value,
            token,
            options);

        // test starting the client
        if (autoConnect)
            client.Connect().Wait();

        return client;
    }

    public static VpnHoodConnect CreateClientConnect(Token token,
        IPacketCapture? packetCapture = default,
        TestDeviceOptions? deviceOptions = default,
        Guid? clientId = default,
        bool autoConnect = true,
        ClientOptions? clientOptions = default,
        ConnectOptions? connectOptions = default)
    {
        clientOptions ??= new ClientOptions();
        packetCapture ??= CreatePacketCapture(deviceOptions);
        clientId ??= Guid.NewGuid();
        if (clientOptions.SessionTimeout == new ClientOptions().SessionTimeout)
            clientOptions.SessionTimeout = TimeSpan.FromSeconds(2); //overwrite default timeout
        clientOptions.SocketFactory = new SocketFactory();
        clientOptions.PacketCaptureIncludeIpRanges = TestIpAddresses.Select(x => new IpRange(x)).ToArray();
        clientOptions.ExcludeLocalNetwork = false;

        var clientConnect = new VpnHoodConnect(
            packetCapture,
            clientId.Value,
            token,
            clientOptions,
            connectOptions);

        // test starting the client
        if (autoConnect)
            clientConnect.Connect().Wait();

        return clientConnect;
    }

    public static VpnHoodApp CreateClientApp(string? appPath = default, TestDeviceOptions? deviceOptions = default)
    {
        //create app
        var appOptions = new AppOptions
        {
            AppDataPath = appPath ?? Path.Combine(WorkingPath, "AppData_" + Guid.NewGuid()),
            LogToConsole = true,
            SessionTimeout = TimeSpan.FromSeconds(2),
            SocketFactory = new TestSocketFactory(false),
            LogAnonymous = false
        };

        var clientApp = VpnHoodApp.Init(new TestAppProvider(deviceOptions), appOptions);
        clientApp.Diagnoser.PingTtl = TestNetProtector.ServerPingTtl;
        clientApp.Diagnoser.HttpTimeout = 2000;
        clientApp.Diagnoser.NsTimeout = 2000;
        clientApp.UserSettings.PacketCaptureIpRangesFilterMode = FilterMode.Include;
        clientApp.UserSettings.PacketCaptureIpRanges = TestIpAddresses.Select(x => new IpRange(x)).ToArray();

        return clientApp;
    }

    private static DateTime GetNetworkTimeByNtp(UdpClient udpClient, IPEndPoint endPoint, int retry)
    {
        for (var i = 0; i < retry + 1; i++)
            try
            {
                return GetNetworkTimeByNtp(udpClient, endPoint);
            }
            catch (Exception ex) when (i < retry)
            {
                VhLogger.Instance.LogWarning($"GetNetworkTimeByNTP failed: {i + 1}, Message: {ex.Message}");
            }

        throw new InvalidProgramException("It should be unreachable!");
    }

    private static DateTime GetNetworkTimeByNtp(UdpClient udpClient, IPEndPoint endPoint)
    {
        var ntpDataRequest = new byte[48];
        ntpDataRequest[0] =
            0x1B; //LeapIndicator = 0 (no warning), VersionNum = 3 (IPv4 only), Mode = 3 (Client Mode)

        udpClient.Send(ntpDataRequest, ntpDataRequest.Length, endPoint);
        var ntpData = udpClient.Receive(ref endPoint);

        var intPart = ((ulong)ntpData[40] << 24) | ((ulong)ntpData[41] << 16) | ((ulong)ntpData[42] << 8) |
                      ntpData[43];
        var fractionPart = ((ulong)ntpData[44] << 24) | ((ulong)ntpData[45] << 16) | ((ulong)ntpData[46] << 8) |
                           ntpData[47];

        var milliseconds = intPart * 1000 + fractionPart * 1000 / 0x100000000L;
        var networkDateTime = new DateTime(1900, 1, 1).AddMilliseconds((long)milliseconds);

        return networkDateTime;
    }

    public static SessionRequestEx CreateSessionRequestEx(Token token, Guid? clientId = null)
    {
        clientId ??= Guid.NewGuid();

        return new SessionRequestEx(token.TokenId,
            new ClientInfo { ClientId = clientId.Value },
            hostEndPoint: token.HostEndPoints!.First(),
            encryptedClientId: Util.EncryptClientId(clientId.Value, token.Secret));
    }

    private static bool _isInit;
    internal static void Init()
    {
        if (_isInit)
            return;
        _isInit = true;

        VhLogger.IsDiagnoseMode = true;
    }
}