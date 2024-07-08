using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client;
using VpnHood.Client.App;
using VpnHood.Client.Device;
using VpnHood.Client.Diagnosing;
using VpnHood.Common;
using VpnHood.Common.Jobs;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Server;
using VpnHood.Server.Access.Configurations;
using VpnHood.Server.Access.Managers;
using VpnHood.Server.Access.Managers.File;
using VpnHood.Server.Access.Messaging;
using VpnHood.Test.AccessManagers;
using VpnHood.Test.Device;
using VpnHood.Test.Services;
using VpnHood.Tunneling;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Test;


internal static class TestHelper
{
    public class TestAppUiContext : IUiContext;
    public static TestWebServer WebServer { get; private set; } = default!;
    public static TestNetFilter NetFilter { get; private set; } = default!;
    private static bool LogVerbose => true;


    private static int _accessItemIndex;

    public static string WorkingPath { get; } = Path.Combine(Path.GetTempPath(), "_test_vpnhood");

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

    public static Task WaitForAppState(VpnHoodApp app, AppConnectionState connectionSate, int timeout = 5000)
    {
        return VhTestUtil.AssertEqualsWait(connectionSate, () => app.State.ConnectionState, "App state didn't reach the expected value.", timeout);
    }

    public static Task WaitForClientState(VpnHoodClient client, ClientState clientState, int timeout = 6000, bool useUpdateStatus = false)
    {
        return VhTestUtil.AssertEqualsWait(clientState,
            async () =>
            {
                if (useUpdateStatus)
                    try { await client.UpdateSessionStatus(); } catch { /*ignore*/ }
                return client.State;
            },
            "Client state didn't reach the expected value.",
            timeout);
    }

    private static Task<PingReply> SendPing(Ping? ping = null, IPAddress? ipAddress = null, int timeout = TestConstants.DefaultTimeout)
    {
        using var pingT = new Ping();
        ping ??= pingT;
        var buffer = new byte[1024];
        new Random().NextBytes(buffer);
        return ping.SendPingAsync(ipAddress ?? TestConstants.PingV4Address1, timeout, buffer);
    }

    private static async Task<bool> SendHttpGet(HttpClient? httpClient = default, Uri? uri = default,
        int timeout = TestConstants.DefaultTimeout)
    {
        uri ??= TestConstants.HttpsUri1;

        using var httpClientT = new HttpClient(new HttpClientHandler
        {
            CheckCertificateRevocationList = false,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        });

        httpClient ??= httpClientT;
        var cancellationTokenSource = new CancellationTokenSource(timeout);

        var requestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

        // fix TLS host; it may map by NetFilter.ProcessRequest
        if (IPEndPoint.TryParse(requestMessage.RequestUri!.Authority, out var ipEndPoint))
            requestMessage.Headers.Host = NetFilter.ProcessRequest(ProtocolType.Tcp, ipEndPoint)!.Address.ToString();

        var response = await httpClient.SendAsync(requestMessage, cancellationTokenSource.Token);
        var res = await response.Content.ReadAsStringAsync(cancellationTokenSource.Token);
        return res.Length > 100;
    }

    public static async Task Test_Ping(Ping? ping = default, IPAddress? ipAddress = default, int timeout = TestConstants.DefaultTimeout)
    {
        var pingReply = await SendPing(ping, ipAddress, timeout);
        if (pingReply.Status != IPStatus.Success)
            throw new PingException($"Ping failed. Status: {pingReply.Status}");
    }

    public static void Test_Dns(UdpClient? udpClient = null, IPEndPoint? nsEndPoint = default, int timeout = 3000)
    {
        var hostEntry = DiagnoseUtil
            .GetHostEntry("www.google.com", nsEndPoint ?? TestConstants.NsEndPoint1, udpClient, timeout).Result;
        Assert.IsNotNull(hostEntry);
        Assert.IsTrue(hostEntry.AddressList.Length > 0);
    }

    public static Task Test_Udp(int timeout = TestConstants.DefaultTimeout)
    {
        return Test_Udp(TestConstants.UdpV4EndPoint1, timeout);
    }

    public static async Task Test_Udp(IPEndPoint udpEndPoint, int timeout = TestConstants.DefaultTimeout)
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

    public static async Task Test_Udp(UdpClient udpClient, IPEndPoint udpEndPoint, int timeout = TestConstants.DefaultTimeout)
    {
        var buffer = new byte[1024];
        new Random().NextBytes(buffer);
        var sentBytes = await udpClient.SendAsync(buffer, udpEndPoint, new CancellationTokenSource(timeout).Token);
        Assert.AreEqual(buffer.Length, sentBytes);

        var res = await udpClient.ReceiveAsync(new CancellationTokenSource(timeout).Token);
        CollectionAssert.AreEquivalent(buffer, res.Buffer);
    }

    public static async Task<bool> Test_Https(HttpClient? httpClient = default, Uri? uri = default,
        int timeout = TestConstants.DefaultTimeout, bool throwError = true)
    {
        if (throwError)
        {
            VhLogger.Instance.LogInformation(GeneralEventId.Test, $"Fetching a test uri. {uri}", uri);
            Assert.IsTrue(await SendHttpGet(httpClient, uri, timeout), $"Could not fetch the test uri: {uri}");
            return true;
        }

        try
        {
            return await SendHttpGet(httpClient, uri, timeout);
        }
        catch
        {
            return false;
        }

    }

    public static IPAddress[] TestIpAddresses
    {
        get
        {
            var addresses = new List<IPAddress>
            {
                TestConstants.NsEndPoint1.Address,
                TestConstants.NsEndPoint2.Address,
                TestConstants.PingV4Address1,
                TestConstants.PingV4Address2,
                TestConstants.PingV6Address1,
                TestConstants.TcpEndPoint1.Address,
                TestConstants.TcpEndPoint2.Address,
                TestConstants.InvalidIp,
                TestConstants.UdpV4EndPoint1.Address,
                TestConstants.UdpV4EndPoint2.Address,
                new ClientOptions().TcpProxyCatcherAddressIpV4
            };
            addresses.AddRange(Dns.GetHostAddresses(TestConstants.HttpsUri1.Host));
            addresses.AddRange(Dns.GetHostAddresses(TestConstants.HttpsUri2.Host));
            return addresses.ToArray();
        }
    }

    public static Token CreateAccessToken(FileAccessManager fileAccessManager,
        int maxClientCount = 1, int maxTrafficByteCount = 0, DateTime? expirationTime = null)
    {
        return fileAccessManager.AccessItem_Create(
            tokenName: $"Test Server {++_accessItemIndex}",
            maxClientCount: maxClientCount,
            maxTrafficByteCount: maxTrafficByteCount,
            expirationTime: expirationTime
        ).Token;
    }

    private static FileAccessManager GetFileAccessManagerFromServer(VpnHoodServer server)
    {
        var accessManager = server.AccessManager;
        return accessManager switch
        {
            FileAccessManager fileAccessManager => fileAccessManager,
            TestHttpAccessManager { EmbedIoAccessManager.BaseAccessManager: FileAccessManager fileAccessManager } => fileAccessManager,
            _ => throw new InvalidOperationException("Could not get FileAccessManager from the server.")
        };
    }

    public static Token CreateAccessToken(VpnHoodServer server,
        int maxClientCount = 1, int maxTrafficByteCount = 0, DateTime? expirationTime = null)
    {
        var fileAccessManager = GetFileAccessManagerFromServer(server);
        return CreateAccessToken(fileAccessManager, maxClientCount, maxTrafficByteCount, expirationTime);
    }

    public static string CreateAccessManagerWorkingDir()
    {
        return Path.Combine(WorkingPath, $"AccessManager_{Guid.NewGuid()}");
    }

    public static TestAccessManager CreateAccessManager(FileAccessManagerOptions? options = null, string? storagePath = null,
        string? serverLocation = null)
    {
        storagePath ??= CreateAccessManagerWorkingDir();
        if (!string.IsNullOrEmpty(serverLocation))
        {
            Directory.CreateDirectory(storagePath);
            File.WriteAllText(Path.Combine(storagePath, "server_location"), serverLocation);
        }

        options ??= CreateFileAccessManagerOptions();
        return new TestAccessManager(storagePath, options);
    }

    public static FileAccessManagerOptions CreateFileAccessManagerOptions(IPEndPoint[]? tcpEndPoints = null)
    {
        var options = new FileAccessManagerOptions
        {
            PublicEndPoints = null, // use TcpEndPoints
            TcpEndPoints = tcpEndPoints ?? [VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback)],
            UdpEndPoints = [new IPEndPoint(IPAddress.Loopback, 0)],
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
            LogAnonymizer = false,
            UseExternalLocationService = false
        };
        return options;
    }

    public static Task<VpnHoodServer> CreateServer(
        IAccessManager? accessManager = null,
        bool autoStart = true, TimeSpan? configureInterval = null, bool useHttpAccessManager = true)
    {
        return CreateServer(accessManager, null,
            autoStart: autoStart,
            configureInterval: configureInterval,
            useHttpAccessManager: useHttpAccessManager);
    }

    public static Task<VpnHoodServer> CreateServer(FileAccessManagerOptions? options, bool autoStart = true,
        TimeSpan? configureInterval = null, bool useHttpAccessManager = true)
    {
        return CreateServer(null, options,
            autoStart: autoStart,
            configureInterval: configureInterval,
            useHttpAccessManager: useHttpAccessManager);
    }

    private static async Task<VpnHoodServer> CreateServer(IAccessManager? accessManager, FileAccessManagerOptions? fileAccessManagerOptions,
        bool autoStart, TimeSpan? configureInterval = null, bool useHttpAccessManager = true)
    {
        if (accessManager != null && fileAccessManagerOptions != null)
            throw new InvalidOperationException($"Could not set both {nameof(accessManager)} and {nameof(fileAccessManagerOptions)}.");

        var autoDisposeAccessManager = false;
        if (accessManager == null)
        {
            accessManager = CreateAccessManager(fileAccessManagerOptions);
            autoDisposeAccessManager = true;
        }

        // use HttpAccessManager 
        if (useHttpAccessManager && accessManager is not TestHttpAccessManager)
        {
            accessManager = TestHttpAccessManager.Create(accessManager, autoDisposeBaseAccessManager: autoDisposeAccessManager);
            autoDisposeAccessManager = true; //Delegate dispose control to TestHttpAccessManager
        }

        // ser server options
        var serverOptions = new ServerOptions
        {
            SocketFactory = new TestSocketFactory(),
            ConfigureInterval = configureInterval ?? new ServerOptions().ConfigureInterval,
            AutoDisposeAccessManager = autoDisposeAccessManager,
            StoragePath = WorkingPath,
            NetFilter = NetFilter,
            PublicIpDiscovery = false //it slows down our tests
        };

        // Create server
        var server = new VpnHoodServer(accessManager, serverOptions);
        if (autoStart)
        {
            await server.Start();
            Assert.AreEqual(ServerState.Ready, server.State);
        }

        return server;
    }

    public static TestDeviceOptions CreateDeviceOptions()
    {
        return new TestDeviceOptions();
    }

    public static IDevice CreateDevice(TestDeviceOptions? options = default)
    {
        options ??= new TestDeviceOptions();
        return new TestDevice(options, false);
    }

    public static IDevice CreateNullDevice()
    {
        return new TestDevice(new TestDeviceOptions(), true);
    }


    public static IPacketCapture CreatePacketCapture(TestDeviceOptions? options = default)
    {
        return CreateDevice(options).CreatePacketCapture(null).Result;
    }

    public static ClientOptions CreateClientOptions(bool useUdp = false)
    {
        return new ClientOptions
        {
            AllowAnonymousTracker = true,
            AllowEndPointTracker = true,
            MaxDatagramChannelCount = 1,
            UseUdpChannel = useUdp,
            Tracker = new TestTracker()
        };
    }

    public static async Task<VpnHoodClient> CreateClient(Token token,
        IPacketCapture? packetCapture = default,
        TestDeviceOptions? deviceOptions = default,
        Guid? clientId = default,
        bool autoConnect = true,
        ClientOptions? clientOptions = default,
        bool throwConnectException = true)
    {
        packetCapture ??= CreatePacketCapture(deviceOptions);
        clientId ??= Guid.NewGuid();
        clientOptions ??= CreateClientOptions();
        if (clientOptions.ConnectTimeout == new ClientOptions().ConnectTimeout) clientOptions.ConnectTimeout = TimeSpan.FromSeconds(3);
        clientOptions.PacketCaptureIncludeIpRanges = TestIpAddresses.Select(IpRange.FromIpAddress).ToOrderedList();
        clientOptions.IncludeLocalNetwork = true;

        var client = new VpnHoodClient(
            packetCapture,
            clientId.Value,
            token,
            clientOptions);

        // test starting the client
        try
        {
            if (autoConnect)
                await client.Connect();
        }
        catch (Exception)
        {
            if (throwConnectException)
                throw;
        }

        return client;
    }

    public static AppOptions CreateAppOptions()
    {
        var tracker = new TestTracker();
        var appOptions = new AppOptions
        {
            StorageFolderPath = Path.Combine(WorkingPath, "AppData_" + Guid.NewGuid()),
            SessionTimeout = TimeSpan.FromSeconds(2),
            AppGa4MeasurementId = null,
            Tracker = tracker,
            UseInternalLocationService = false,
            UseExternalLocationService = false,
            AllowEndPointTracker = true,
            LogVerbose = LogVerbose,
            ServerQueryTimeout = TimeSpan.FromSeconds(2),
            AutoDiagnose = false,
            SingleLineConsoleLog = false,
            AdOptions = new AppAdOptions
            {
                ShowAdPostDelay = TimeSpan.Zero,
                LoadAdPostDelay = TimeSpan.Zero
            }
        };
        return appOptions;
    }

    public static VpnHoodApp CreateClientApp(TestDeviceOptions? deviceOptions = default, AppOptions? appOptions = default)
    {
        //create app
        appOptions ??= CreateAppOptions();

        var device = deviceOptions != null ? CreateDevice(deviceOptions) : CreateNullDevice();
        var clientApp = VpnHoodApp.Init(device, appOptions);
        clientApp.Diagnoser.HttpTimeout = 2000;
        clientApp.Diagnoser.NsTimeout = 2000;
        clientApp.UserSettings.PacketCaptureIncludeIpRanges = TestIpAddresses.Select(x => new IpRange(x)).ToArray();
        clientApp.UserSettings.Logging.LogAnonymous = false;
        clientApp.TcpTimeout = TimeSpan.FromSeconds(2);
        ActiveUiContext.Context = new TestAppUiContext();

        return clientApp;
    }

    public static SessionRequestEx CreateSessionRequestEx(Token token, Guid? clientId = null)
    {
        clientId ??= Guid.NewGuid();
        return new SessionRequestEx
        {
            TokenId = token.TokenId,
            ClientInfo = new ClientInfo
            {
                ClientId = clientId.Value,
                UserAgent = "Test",
                ClientVersion = "1.0.0",
                ProtocolVersion = 4
            },
            HostEndPoint = token.ServerToken.HostEndPoints!.First(),
            EncryptedClientId = VhUtil.EncryptClientId(clientId.Value, token.Secret),
            ClientIp = null,
            ExtraData = null
        };
    }

    private static bool _isInit;
    internal static void Init()
    {
        if (_isInit) return;
        _isInit = true;

        TunnelDefaults.TcpGracefulTimeout = TimeSpan.FromSeconds(10);
        VhLogger.Instance = VhLogger.CreateConsoleLogger(true);
        VhLogger.IsDiagnoseMode = true;
        VhLogger.IsAnonymousMode = false;
        WebServer = TestWebServer.Create();
        NetFilter = new TestNetFilter();
        NetFilter.Init([
            Tuple.Create(ProtocolType.Tcp, TestConstants.TcpEndPoint1, WebServer.HttpV4EndPoint1),
            Tuple.Create(ProtocolType.Tcp, TestConstants.TcpEndPoint2, WebServer.HttpV4EndPoint2),
            Tuple.Create(ProtocolType.Tcp, TestConstants.HttpsEndPoint1, WebServer.HttpsV4EndPoint1),
            Tuple.Create(ProtocolType.Tcp, TestConstants.HttpsEndPoint2, WebServer.HttpsV4EndPoint2),
            Tuple.Create(ProtocolType.Udp, TestConstants.UdpV4EndPoint1, WebServer.UdpV4EndPoint1),
            Tuple.Create(ProtocolType.Udp, TestConstants.UdpV4EndPoint2, WebServer.UdpV4EndPoint2),
            Tuple.Create(ProtocolType.Udp, TestConstants.UdpV6EndPoint1, WebServer.UdpV6EndPoint1),
            Tuple.Create(ProtocolType.Udp, TestConstants.UdpV6EndPoint2, WebServer.UdpV6EndPoint2),
            Tuple.Create(ProtocolType.Icmp, new IPEndPoint(TestConstants.PingV4Address1, 0), IPEndPoint.Parse("127.0.0.1:0")),
            Tuple.Create(ProtocolType.Icmp, new IPEndPoint(TestConstants.PingV4Address2, 0), IPEndPoint.Parse("127.0.0.2:0")),
            Tuple.Create(ProtocolType.IcmpV6, new IPEndPoint(TestConstants.PingV6Address1, 0), IPEndPoint.Parse("[::1]:0"))
        ]);
        FastDateTime.Precision = TimeSpan.FromMilliseconds(1);
        JobRunner.Default.Interval = TimeSpan.FromMilliseconds(200);
        JobSection.DefaultInterval = TimeSpan.FromMilliseconds(200);
    }
    public static string GetParentDirectory(string path, int level = 1)
    {
        for (var i = 0; i < level; i++)
            path = Path.GetDirectoryName(path) ?? throw new Exception("Invalid path");

        return path;
    }
}