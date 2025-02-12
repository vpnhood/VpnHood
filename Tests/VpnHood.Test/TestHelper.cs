using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.Jobs;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Net;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Server;
using VpnHood.Core.Server.Abstractions;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers;
using VpnHood.Core.Server.Access.Messaging;
using VpnHood.Core.Tunneling;
using VpnHood.Test.AccessManagers;
using VpnHood.Test.Device;
using VpnHood.Test.Providers;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Test;

public class TestHelper : IDisposable
{
    public class TestAppUiContext : IUiContext
    {
        public bool IsActive => true;
    }

    public static string WorkingPath { get; } = Path.Combine(Path.GetTempPath(), "_test_vpnhood");
    public static TestWebServer WebServer { get; private set; } = null!;
    public static TestNetFilter NetFilter { get; private set; } = null!;
    public static bool LogVerbose => true;
    private static bool? _isIpV6Supported;
    private static int _accessTokenIndex;

    public static async Task<bool> IsIpV6Supported()
    {
        _isIpV6Supported ??= await IPAddressUtil.IsIpv6Supported();
        return _isIpV6Supported.Value;
    }

    public static void Cleanup()
    {
        try {
            if (Directory.Exists(WorkingPath))
                Directory.Delete(WorkingPath, true);
        }
        catch {
            // ignored
        }
    }

    public static Task WaitForClientState(VpnHoodClient client, ClientState clientState, int timeout = 6000,
        bool useUpdateStatus = false)
    {
        return VhTestUtil.AssertEqualsWait(clientState,
            async () => {
                if (useUpdateStatus)
                    try {
                        await client.UpdateSessionStatus();
                    }
                    catch {
                        /*ignore*/
                    }

                return client.State;
            },
            "Client state didn't reach the expected value.",
            timeout);
    }

    private static Task<PingReply> SendPing(Ping? ping = null, IPAddress? ipAddress = null,
        int timeout = TestConstants.DefaultTimeout)
    {
        using var pingT = new Ping();
        ping ??= pingT;
        var buffer = new byte[1024];
        new Random().NextBytes(buffer);
        return ping.SendPingAsync(ipAddress ?? TestConstants.PingV4Address1, timeout, buffer);
    }

    private static async Task<bool> SendHttpGet(Uri uri, int timeout = TestConstants.DefaultTimeout)
    {
        using var httpClient = new HttpClient(new HttpClientHandler {
            CheckCertificateRevocationList = false,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        });

        return await SendHttpGet(httpClient, uri, timeout);
    }

    private static async Task<bool> SendHttpGet(HttpClient httpClient, Uri uri,
        int timeout = TestConstants.DefaultTimeout)
    {
        var cancellationTokenSource = new CancellationTokenSource(timeout);
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

        // fix TLS host; it may map by NetFilter.ProcessRequest
        if (IPEndPoint.TryParse(requestMessage.RequestUri!.Authority, out var ipEndPoint))
            requestMessage.Headers.Host = NetFilter.ProcessRequest(ProtocolType.Tcp, ipEndPoint)!.Address.ToString();

        var response = await httpClient.SendAsync(requestMessage, cancellationTokenSource.Token);
        var res = await response.Content.ReadAsStringAsync(cancellationTokenSource.Token);
        return res.Length > 100;
    }

    public static async Task Test_Ping(Ping? ping = null, IPAddress? ipAddress = null,
        int timeout = TestConstants.DefaultTimeout)
    {
        var pingReply = await SendPing(ping, ipAddress, timeout);
        if (pingReply.Status != IPStatus.Success)
            throw new PingException($"Ping failed. Status: {pingReply.Status}");
    }

    public static async Task Test_Dns(IPEndPoint? nsEndPoint = null, int timeout = 3000,
        CancellationToken cancellationToken = default)
    {
        var hostEntry = await DnsResolver.GetHostEntry("www.google.com", nsEndPoint ?? TestConstants.NsEndPoint1,
            timeout, cancellationToken);
        Assert.IsNotNull(hostEntry);
        Assert.IsTrue(hostEntry.AddressList.Length > 0);
    }

    public static Task Test_Udp(int timeout = TestConstants.DefaultTimeout)
    {
        return Test_Udp(TestConstants.UdpV4EndPoint1, timeout);
    }

    public static async Task Test_Udp(IPEndPoint udpEndPoint, int timeout = TestConstants.DefaultTimeout)
    {
        if (udpEndPoint.AddressFamily == AddressFamily.InterNetwork) {
            using var udpClientIpV4 = new UdpClient(AddressFamily.InterNetwork);
            await Test_Udp(udpClientIpV4, udpEndPoint, timeout);
        }

        else if (udpEndPoint.AddressFamily == AddressFamily.InterNetworkV6) {
            using var udpClientIpV6 = new UdpClient(AddressFamily.InterNetworkV6);
            await Test_Udp(udpClientIpV6, udpEndPoint, timeout);
        }
    }

    public static async Task Test_Udp(UdpClient udpClient, IPEndPoint udpEndPoint,
        int timeout = TestConstants.DefaultTimeout)
    {
        var buffer = new byte[1024];
        new Random().NextBytes(buffer);
        var sentBytes = await udpClient.SendAsync(buffer, udpEndPoint, new CancellationTokenSource(timeout).Token);
        Assert.AreEqual(buffer.Length, sentBytes);

        var res = await udpClient.ReceiveAsync(new CancellationTokenSource(timeout).Token);
        CollectionAssert.AreEquivalent(buffer, res.Buffer);
    }

    public static async Task<bool> Test_Https(Uri? uri = null,
        int timeout = TestConstants.DefaultTimeout, bool throwError = true)
    {
        uri ??= TestConstants.HttpsUri1;

        if (throwError) {
            VhLogger.Instance.LogInformation(GeneralEventId.Test, "Fetching a test uri. Url: {uri}", uri);
            Assert.IsTrue(await SendHttpGet(uri, timeout), $"Could not fetch the test uri: {uri}");
            return true;
        }

        try {
            return await SendHttpGet(uri, timeout);
        }
        catch {
            return false;
        }
    }

    public static IPAddress[] TestIpAddresses {
        get {
            var addresses = new List<IPAddress> {
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
                ClientOptions.Default.TcpProxyCatcherAddressIpV4
            };
            addresses.AddRange(Dns.GetHostAddresses(TestConstants.HttpsUri1.Host));
            addresses.AddRange(Dns.GetHostAddresses(TestConstants.HttpsUri2.Host));
            return addresses.ToArray();
        }
    }

    public static Token CreateAccessToken(FileAccessManager fileAccessManager,
        int maxClientCount = 1, int maxTrafficByteCount = 0, DateTime? expirationTime = null)
    {
        var accessToken = fileAccessManager.AccessTokenService.Create(
            tokenName: $"Test Server {++_accessTokenIndex}",
            maxClientCount: maxClientCount,
            maxTrafficByteCount: maxTrafficByteCount,
            expirationTime: expirationTime
        );

        return fileAccessManager.GetToken(accessToken);
    }

    private static FileAccessManager GetFileAccessManagerFromServer(VpnHoodServer server)
    {
        var accessManager = server.AccessManager;
        return accessManager switch {
            FileAccessManager fileAccessManager => fileAccessManager,
            TestHttpAccessManager {
                EmbedIoAccessManager.BaseAccessManager: FileAccessManager fileAccessManager
            } => fileAccessManager,
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

    public static TestAccessManager CreateAccessManager(FileAccessManagerOptions? options = null,
        string? storagePath = null,
        string? serverLocation = null)
    {
        storagePath ??= CreateAccessManagerWorkingDir();
        if (!string.IsNullOrEmpty(serverLocation)) {
            Directory.CreateDirectory(storagePath);
            File.WriteAllText(Path.Combine(storagePath, "server_location"), serverLocation);
        }

        options ??= CreateFileAccessManagerOptions();
        return new TestAccessManager(storagePath, options);
    }

    public static FileAccessManagerOptions CreateFileAccessManagerOptions(IPEndPoint[]? tcpEndPoints = null)
    {
        var options = new FileAccessManagerOptions {
            IsUnitTest = true,
            PublicEndPoints = null, // use TcpEndPoints
            TcpEndPoints = tcpEndPoints ?? [VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback)],
            UdpEndPoints = [new IPEndPoint(IPAddress.Loopback, 0)],
            TrackingOptions = new TrackingOptions {
                TrackClientIp = true,
                TrackDestinationIp = true,
                TrackDestinationPort = true,
                TrackLocalPort = true
            },
            SessionOptions = {
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
        bool autoStart = true, TimeSpan? configureInterval = null, bool useHttpAccessManager = true,
        INetConfigurationProvider? netConfigurationProvider = null,
        ISwapMemoryProvider? swapMemoryProvider = null)
    {
        return CreateServer(accessManager, null,
            autoStart: autoStart,
            configureInterval: configureInterval,
            useHttpAccessManager: useHttpAccessManager,
            netConfigurationProvider: netConfigurationProvider,
            swapMemoryProvider: swapMemoryProvider);
    }

    public static Task<VpnHoodServer> CreateServer(FileAccessManagerOptions? options, bool autoStart = true,
        TimeSpan? configureInterval = null, bool useHttpAccessManager = true,
        ITunProvider? tunProvider = null)
    {
        return CreateServer(null, options,
            autoStart: autoStart,
            configureInterval: configureInterval,
            useHttpAccessManager: useHttpAccessManager,
            tunProvider: tunProvider);
    }

    private static async Task<VpnHoodServer> CreateServer(IAccessManager? accessManager,
        FileAccessManagerOptions? fileAccessManagerOptions,
        bool autoStart, TimeSpan? configureInterval = null, bool useHttpAccessManager = true,
        INetConfigurationProvider? netConfigurationProvider = null,
        ISwapMemoryProvider? swapMemoryProvider = null,
        ITunProvider? tunProvider = null)
    {
        if (accessManager != null && fileAccessManagerOptions != null)
            throw new InvalidOperationException(
                $"Could not set both {nameof(accessManager)} and {nameof(fileAccessManagerOptions)}.");

        var autoDisposeAccessManager = false;
        if (accessManager == null) {
            accessManager = CreateAccessManager(fileAccessManagerOptions);
            autoDisposeAccessManager = true;
        }

        // use HttpAccessManager 
        if (useHttpAccessManager && accessManager is not TestHttpAccessManager) {
            accessManager =
                TestHttpAccessManager.Create(accessManager, autoDisposeBaseAccessManager: autoDisposeAccessManager);
            autoDisposeAccessManager = true; //Delegate dispose control to TestHttpAccessManager
        }

        // ser server options
        var serverOptions = new ServerOptions {
            SocketFactory = new TestSocketFactory(),
            ConfigureInterval = configureInterval ?? new ServerOptions().ConfigureInterval,
            AutoDisposeAccessManager = autoDisposeAccessManager,
            StoragePath = WorkingPath,
            NetFilter = NetFilter,
            NetConfigurationProvider = netConfigurationProvider,
            SwapMemoryProvider = swapMemoryProvider,
            TunProvider = tunProvider,
            PublicIpDiscovery = false //it slows down our tests
        };

        // Create server
        var server = new VpnHoodServer(accessManager, serverOptions);
        if (autoStart) {
            await server.Start();
            Assert.AreEqual(ServerState.Ready, server.State);
        }

        return server;
    }

    public static TestVpnAdapterOptions CreateTestVpnAdapterOptions()
    {
        return new TestVpnAdapterOptions();
    }

    public static TestVpnAdapter CreateTestVpnAdapter(TestVpnAdapterOptions? options = null)
    {
        options ??= CreateTestVpnAdapterOptions();
        return new TestVpnAdapter(options);
    }


    public static TestDevice CreateDevice(TestVpnAdapterOptions? options = null)
    {
        options ??= CreateTestVpnAdapterOptions();
        return new TestDevice(() => new TestVpnAdapter(options));
    }


    public static ClientOptions CreateClientOptions(Token token, bool useUdpChannel = false, string? clientId = null)
    {
        return new ClientOptions {
            ClientId = clientId ?? Guid.NewGuid().ToString(),
            AllowAnonymousTracker = true,
            AllowEndPointTracker = true,
            MaxDatagramChannelCount = 1,
            UseUdpChannel = useUdpChannel,
            VpnAdapterIncludeIpRanges = TestIpAddresses.Select(IpRange.FromIpAddress).ToArray(),
            IncludeLocalNetwork = true,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            SessionName = "UnitTestSession",
            AccessKey = token.ToAccessKey()
        };
    }

    public static Task<VpnHoodClient> CreateClient(Token token,
        IVpnAdapter? vpnAdapter = null,
        string? clientId = null,
        bool autoConnect = true)
    {
        var clientOptions = CreateClientOptions(token, clientId: clientId);
        return CreateClient(clientOptions, vpnAdapter, autoConnect);
    }

    public static async Task<VpnHoodClient> CreateClient(
        ClientOptions clientOptions,
        IVpnAdapter? vpnAdapter = null,
        bool autoConnect = true)
    {
        vpnAdapter ??= new TestVpnAdapter(new TestVpnAdapterOptions());
        var client = new VpnHoodClient(vpnAdapter, new TestSocketFactory(), new TestTrackerProvider(), clientOptions);

        // test starting the client
        if (autoConnect)
            await client.Connect();

        return client;
    }


    public static SessionRequestEx CreateSessionRequestEx(Token token, string? clientId = null)
    {
        clientId ??= Guid.NewGuid().ToString();
        return new SessionRequestEx {
            TokenId = token.TokenId,
            ClientInfo = new ClientInfo {
                ClientId = clientId,
                UserAgent = "Test",
                ClientVersion = "1.0.0",
                ProtocolVersion = 4
            },
            HostEndPoint = token.ServerToken.HostEndPoints!.First(),
            EncryptedClientId = VhUtils.EncryptClientId(clientId, token.Secret),
            ClientIp = null,
            ExtraData = null
        };
    }

    private static bool _isInit;

    public static void Init()
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
            Tuple.Create(ProtocolType.Icmp, new IPEndPoint(TestConstants.PingV4Address1, 0),
                IPEndPoint.Parse("127.0.0.1:0")),
            Tuple.Create(ProtocolType.Icmp, new IPEndPoint(TestConstants.PingV4Address2, 0),
                IPEndPoint.Parse("127.0.0.2:0")),
            Tuple.Create(ProtocolType.IcmpV6, new IPEndPoint(TestConstants.PingV6Address1, 0),
                IPEndPoint.Parse("[::1]:0"))
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

    public virtual void Dispose()
    {
    }
}