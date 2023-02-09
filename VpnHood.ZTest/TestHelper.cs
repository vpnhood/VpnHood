using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client;
using VpnHood.Client.App;
using VpnHood.Client.Device;
using VpnHood.Client.Diagnosing;
using VpnHood.Common;
using VpnHood.Common.Converters;
using VpnHood.Common.JobController;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Server;
using VpnHood.Server.Messaging;
using VpnHood.Server.Providers.FileAccessServerProvider;
using VpnHood.Test.Factory;
using VpnHood.Tunneling.Factory;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Test;

internal static class TestHelper
{
    private const int DefaultTimeout = 30000;
    public static readonly Uri TEST_HttpsUri1 = new("https://www.quad9.net/");
    public static readonly Uri TEST_HttpsUri2 = new("https://www.shell.com/");
    public static readonly IPEndPoint TEST_NsEndPoint1 = IPEndPoint.Parse("1.1.1.1:53");
    public static readonly IPEndPoint TEST_NsEndPoint2 = IPEndPoint.Parse("1.0.0.1:53");
    public static readonly IPEndPoint TEST_TcpEndPoint1 = IPEndPoint.Parse("198.18.0.1:80");
    public static readonly IPEndPoint TEST_TcpEndPoint2 = IPEndPoint.Parse("198.18.0.2:80");
    public static readonly IPEndPoint TEST_HttpsEndPoint1 = IPEndPoint.Parse("198.18.0.1:3030");
    public static readonly IPEndPoint TEST_HttpsEndPoint2 = IPEndPoint.Parse("198.18.0.2:3030");
    public static readonly IPEndPoint TEST_UdpV4EndPoint1 = IPEndPoint.Parse("198.18.10.1:63100");
    public static readonly IPEndPoint TEST_UdpV4EndPoint2 = IPEndPoint.Parse("198.18.10.2:63101");
    public static readonly IPEndPoint TEST_UdpV6EndPoint1 = IPEndPoint.Parse("[2001:4860:4866::2223]:63100");
    public static readonly IPEndPoint TEST_UdpV6EndPoint2 = IPEndPoint.Parse("[2001:4860:4866::2223]:63101");
    public static readonly IPAddress TEST_PingV4Address1 = IPAddress.Parse("198.18.20.1");
    public static readonly IPAddress TEST_PingV4Address2 = IPAddress.Parse("198.18.20.2");
    public static readonly IPAddress TEST_PingV6Address1 = IPAddress.Parse("2001:4860:4866::2200");

    public static readonly Uri TEST_InvalidUri = new("https://DBBC5764-D452-468F-8301-4B315507318F.zz");
    public static readonly IPAddress TEST_InvalidIp = IPAddress.Parse("192.168.199.199");
    public static readonly IPEndPoint TEST_InvalidEp = IPEndPointConverter.Parse("192.168.199.199:9999");
    public static TestWebServer WebServer { get; private set; } = default!;
    public static TestNetFilter NetFilter { get; private set; } = default!;

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

    public static Task WaitForClientStateAsync(VpnHoodClient client, ClientState clientState, int timeout = 6000)
    {
        return AssertEqualsWait(clientState, () => client.State, "Client state didn't reach the expected value.", timeout);
    }

    private static PingReply SendPing(Ping? ping = null, IPAddress? ipAddress = null, int timeout = DefaultTimeout)
    {
        using var pingT = new Ping();
        ping ??= pingT;
        var buffer = new byte[1024];
        new Random().NextBytes(buffer);
        return ping.Send(ipAddress ?? TEST_PingV4Address1, timeout, buffer);
    }

    private static async Task<bool> SendHttpGet(HttpClient? httpClient = default, Uri? uri = default, int timeout = DefaultTimeout)
    {
        uri ??= TEST_HttpsUri1;

        using var httpClientT = new HttpClient(new HttpClientHandler
        {
            CheckCertificateRevocationList = false,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        });

        httpClient ??= httpClientT;
        var cancellationTokenSource = new CancellationTokenSource(timeout);
        
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

        // fix TLS host; it may map by NetFilter.ProcessRequest
        if (IPEndPoint.TryParse(requestMessage.RequestUri!.Authority, out var ipEndPoint ))
            requestMessage.Headers.Host = NetFilter.ProcessRequest(ProtocolType.Tcp, ipEndPoint)!.Address.ToString();
        
        var response = await httpClient.SendAsync(requestMessage, cancellationTokenSource.Token);
        var res = await response.Content.ReadAsStringAsync(cancellationTokenSource.Token);
        return res.Length > 100;
    }

    public static void Test_Ping(Ping? ping = default, IPAddress? ipAddress = default, int timeout = DefaultTimeout)
    {
        var pingReply = SendPing(ping, ipAddress, timeout);
        Assert.AreEqual(IPStatus.Success, pingReply.Status);
    }

    public static void Test_Dns(UdpClient? udpClient = null, IPEndPoint? nsEndPoint = default, int timeout = 3000)
    {
        var hostEntry = DiagnoseUtil
            .GetHostEntry("www.google.com", nsEndPoint ?? TEST_NsEndPoint1, udpClient, timeout).Result;
        Assert.IsNotNull(hostEntry);
        Assert.IsTrue(hostEntry.AddressList.Length > 0);
    }

    public static async Task Test_Udp(int timeout = DefaultTimeout)
    {
        await Test_Udp(TEST_UdpV4EndPoint1, timeout);
    }

    public static async Task Test_Udp(IPEndPoint udpEndPoint, int timeout = DefaultTimeout)
    {
        if (udpEndPoint.AddressFamily == AddressFamily.InterNetwork)
        {
            using var udpClientIpV4 = new UdpClient(AddressFamily.InterNetwork);
            await Test_Udp(udpClientIpV4, udpEndPoint, timeout);
        }

        else if (udpEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
        {
            using var udpClientIpV6 = new UdpClient(AddressFamily.InterNetworkV6);
            await Test_Udp(udpClientIpV6, udpEndPoint, timeout);
        }
    }
 
    public static async Task Test_Udp(UdpClient udpClient, IPEndPoint udpEndPoint, int timeout = DefaultTimeout)
    {
        var buffer = new byte[1024];
        new Random().NextBytes(buffer);
        var sentBytes = await udpClient.SendAsync(buffer, udpEndPoint, new CancellationTokenSource(timeout).Token);
        Assert.AreEqual(buffer.Length, sentBytes);

        var res = await udpClient.ReceiveAsync(new CancellationTokenSource(timeout).Token);
        CollectionAssert.AreEquivalent(buffer, res.Buffer);
    }

    public static void Test_Https(HttpClient? httpClient = default, Uri? uri = default, int timeout = 3000)
    {
        Test_HttpsAsync(httpClient, uri, timeout).Wait();
    }

    public static async Task Test_HttpsAsync(HttpClient? httpClient = default, Uri? uri = default, int timeout = DefaultTimeout)
    {
        Assert.IsTrue(await SendHttpGet(httpClient, uri, timeout), "Https get doesn't work!");
    }

    public static IPAddress[] TestIpAddresses
    {
        get
        {
            var addresses = new List<IPAddress>
            {
                TEST_NsEndPoint1.Address,
                TEST_NsEndPoint2.Address,
                TEST_PingV4Address1,
                TEST_PingV4Address2,
                TEST_PingV6Address1,
                TEST_TcpEndPoint1.Address,
                TEST_TcpEndPoint2.Address,
                TEST_InvalidIp,
                TEST_UdpV4EndPoint1.Address,
                TEST_UdpV4EndPoint2.Address,
                new ClientOptions().TcpProxyCatcherAddressIpV4
            };
            addresses.AddRange(Dns.GetHostAddresses(TEST_HttpsUri1.Host));
            addresses.AddRange(Dns.GetHostAddresses(TEST_HttpsUri2.Host));
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
            TcpEndPoints = new[] { Util.GetFreeTcpEndPoint(IPAddress.Loopback) },
            TrackingOptions = new TrackingOptions
            {
                TrackClientIp = true,
                TrackDestinationIp = true,
                TrackDestinationPort = true,
                TrackLocalPort = true
            },
            SessionOptions =
            {
                SyncCacheSize = 50,
                SyncInterval = TimeSpan.FromMilliseconds(100)
            },
        };
        return options;
    }

    public static VpnHoodServer CreateServer(IAccessServer? accessServer = null, bool autoStart = true, TimeSpan? configureInterval = null)
    {
        return CreateServer(accessServer, null, autoStart);
    }

    public static VpnHoodServer CreateServer(FileAccessServerOptions? options, bool autoStart = true, TimeSpan? configureInterval = null)
    {
        return CreateServer(null, options, autoStart);
    }

    private static VpnHoodServer CreateServer(IAccessServer? accessServer, FileAccessServerOptions? fileAccessServerOptions, bool autoStart,
        TimeSpan? configureInterval = null)
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
            ConfigureInterval = configureInterval ?? new ServerOptions().ConfigureInterval,
            AutoDisposeAccessServer = autoDisposeAccessServer,
            StoragePath = WorkingPath,
            NetFilter = NetFilter,
            PublicIpDiscovery = false, //it slows down our tests
        };

        // Create server
        var server = new VpnHoodServer(accessServer, serverOptions);
        if (autoStart)
        {
            server.Start().Wait();
            Assert.AreEqual(ServerState.Ready, server.State);
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

    public static AppOptions CreateClientAppOptions()
    {
        var appOptions = new AppOptions
        {
            AppDataPath = Path.Combine(WorkingPath, "AppData_" + Guid.NewGuid()),
            LogToConsole = true,
            SessionTimeout = TimeSpan.FromSeconds(2),
            SocketFactory = new TestSocketFactory(false),
            LogAnonymous = false
        };
        return appOptions;
    }

    public static VpnHoodApp CreateClientApp(TestDeviceOptions? deviceOptions = default, AppOptions? appOptions = default)
    {
        //create app
        appOptions ??= CreateClientAppOptions();

        var clientApp = VpnHoodApp.Init(new TestAppProvider(deviceOptions), appOptions);
        clientApp.Diagnoser.PingTtl = TestNetProtector.ServerPingTtl;
        clientApp.Diagnoser.HttpTimeout = 2000;
        clientApp.Diagnoser.NsTimeout = 2000;
        clientApp.UserSettings.PacketCaptureIpRangesFilterMode = FilterMode.Include;
        clientApp.UserSettings.PacketCaptureIpRanges = TestIpAddresses.Select(x => new IpRange(x)).ToArray();

        return clientApp;
    }

    public static SessionRequestEx CreateSessionRequestEx(Token token, Guid? clientId = null)
    {
        clientId ??= Guid.NewGuid();

        return new SessionRequestEx(token.TokenId,
            new ClientInfo { ClientId = clientId.Value },
            hostEndPoint: token.HostEndPoints!.First(),
            encryptedClientId: Util.EncryptClientId(clientId.Value, token.Secret));
    }

    public static async Task<bool> WaitForValue<T, TValue>(T obj, object? expectedValue, Func<T, TValue?> valueFactory, int timeout = 5000)
    {
        const int waitTime = 100;
        for (var elapsed = 0; elapsed < timeout; elapsed += waitTime)
        {
            if (Equals(valueFactory(obj), expectedValue))
                return true;
            await Task.Delay(waitTime);
        }

        return false;
    }

    public static async Task AssertEqualsWait<T, TValue>(T obj, TValue? expectedValue, Func<T, TValue> valueFactory, string? message = null, int timeout = 5000)
    {
        await WaitForValue(obj, expectedValue, valueFactory, timeout);

        if (message != null)
            Assert.AreEqual(expectedValue, valueFactory(obj), message);
        else
            Assert.AreEqual(expectedValue, valueFactory(obj));
    }

    public static async Task<bool> WaitForValue<TValue>(object? expectedValue, Func<TValue?> valueFactory, int timeout = 5000)
    {
        const int waitTime = 100;
        for (var elapsed = 0; elapsed < timeout; elapsed += waitTime)
        {
            if (Equals(valueFactory(), expectedValue))
                return true;
            await Task.Delay(waitTime);
        }

        return false;
    }

    public static async Task AssertEqualsWait<TValue>(TValue? expectedValue, Func<TValue> valueFactory, string? message = null, int timeout = 5000)
    {
        await WaitForValue(expectedValue, valueFactory, timeout);

        if (message != null)
            Assert.AreEqual(expectedValue, valueFactory(), message);
        else
            Assert.AreEqual(expectedValue, valueFactory());
    }


    private static bool _isInit;
    internal static void Init()
    {
        if (_isInit) return;
        _isInit = true;

        VhLogger.Instance = VhLogger.CreateConsoleLogger(true);
        VhLogger.IsDiagnoseMode = true;
        WebServer = TestWebServer.Create();
        NetFilter = new TestNetFilter();
        NetFilter.Init(new[]
        {
            Tuple.Create(ProtocolType.Tcp, TEST_TcpEndPoint1, WebServer.HttpV4EndPoint1),
            Tuple.Create(ProtocolType.Tcp, TEST_TcpEndPoint2, WebServer.HttpV4EndPoint2),
            Tuple.Create(ProtocolType.Tcp, TEST_HttpsEndPoint1, WebServer.HttpsV4EndPoint1),
            Tuple.Create(ProtocolType.Tcp, TEST_HttpsEndPoint2, WebServer.HttpsV4EndPoint2),
            Tuple.Create(ProtocolType.Udp, TEST_UdpV4EndPoint1, WebServer.UdpV4EndPoint1),
            Tuple.Create(ProtocolType.Udp, TEST_UdpV4EndPoint2, WebServer.UdpV4EndPoint2),
            Tuple.Create(ProtocolType.Udp, TEST_UdpV6EndPoint1, WebServer.UdpV6EndPoint1),
            Tuple.Create(ProtocolType.Udp, TEST_UdpV6EndPoint2, WebServer.UdpV6EndPoint2),
            Tuple.Create(ProtocolType.Icmp, new IPEndPoint(TEST_PingV4Address1, 0), IPEndPoint.Parse("127.0.0.1:0")),
            Tuple.Create(ProtocolType.Icmp, new IPEndPoint(TEST_PingV4Address2, 0), IPEndPoint.Parse("127.0.0.2:0")),
            Tuple.Create(ProtocolType.IcmpV6, new IPEndPoint(TEST_PingV6Address1, 0), IPEndPoint.Parse("[::1]:0")),
        });
        FastDateTime.Precision = TimeSpan.FromMilliseconds(1);
        JobRunner.Default.Interval = TimeSpan.FromMilliseconds(200);
    }
}