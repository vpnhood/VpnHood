using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Packets;
using VpnHood.Core.Server;
using VpnHood.Core.Server.Abstractions;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Server.Access.Managers.FileAccessManagement;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Proxies;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Core.VpnAdapters.Abstractions;
using VpnHood.Test.AccessManagers;
using VpnHood.Test.Device;
using VpnHood.Test.Providers;

namespace VpnHood.Test;

public class TestHelper : IDisposable
{
    private static readonly string AssemblyWorkingPath = Path.Combine(Path.GetTempPath(), "VpnHood.Test");
    public class TestAppUiContext : IUiContext
    {
        public bool IsActive => true;
        public bool IsDestroyed => false;
    }

    public string WorkingPath { get; } = Path.Combine(AssemblyWorkingPath, Guid.CreateVersion7().ToString());
    public TestWebServer WebServer { get; }
    public TestNetFilter NetFilter { get; }
    private bool? _isIpV6Supported;
    private int _accessTokenIndex;

    public TestHelper()
    {
        TunnelDefaults.TcpGracefulTimeout = TimeSpan.FromSeconds(10);
        VhLogger.Instance = VhLogger.CreateConsoleLogger(); // min level is controlled by VhLogger.MinLevel
        VhLogger.MinLogLevel = LogLevel.Debug;
        VhLogger.IsAnonymousMode = false;
        WebServer = TestWebServer.Create();
        NetFilter = new TestNetFilter();
        NetFilter.Init([TestConstants.BlockedIp], 
        [
            Tuple.Create(IpProtocol.Tcp, TestConstants.TcpEndPoint1, WebServer.HttpV4EndPoint1),
            Tuple.Create(IpProtocol.Tcp, TestConstants.TcpEndPoint2, WebServer.HttpV4EndPoint2),
            Tuple.Create(IpProtocol.Tcp, TestConstants.HttpsEndPoint1, WebServer.HttpsV4EndPoint1),
            Tuple.Create(IpProtocol.Tcp, TestConstants.HttpsEndPoint2, WebServer.HttpsV4EndPoint2),
            Tuple.Create(IpProtocol.Tcp, TestConstants.TcpRefusedEndPoint, WebServer.HttpsV4RefusedEndPoint1),
            Tuple.Create(IpProtocol.Udp, TestConstants.UdpV4EndPoint1, WebServer.UdpV4EndPoint1),
            Tuple.Create(IpProtocol.Udp, TestConstants.UdpV4EndPoint2, WebServer.UdpV4EndPoint2),
            Tuple.Create(IpProtocol.Udp, TestConstants.UdpV6EndPoint1, WebServer.UdpV6EndPoint1),
            Tuple.Create(IpProtocol.Udp, TestConstants.UdpV6EndPoint2, WebServer.UdpV6EndPoint2),
            Tuple.Create(IpProtocol.IcmpV4, new IPEndPoint(TestConstants.PingV4Address1, 0),
                IPEndPoint.Parse("127.0.0.1:0")),
            Tuple.Create(IpProtocol.IcmpV4, new IPEndPoint(TestConstants.PingV4Address2, 0),
                IPEndPoint.Parse("127.0.0.2:0")),
            Tuple.Create(IpProtocol.IcmpV6, new IPEndPoint(TestConstants.PingV6Address1, 0),
                IPEndPoint.Parse("[::1]:0"))
        ]);
        FastDateTime.Precision = TimeSpan.FromMilliseconds(1);
        JobOptions.DefaultPeriod = TimeSpan.FromMilliseconds(1000);
        JobRunner.SlowInstance.Interval = TimeSpan.FromMilliseconds(200);
        JobRunner.FastInstance.Interval = TimeSpan.FromMilliseconds(200);
    }

    public async Task<bool> IsIpV6Supported()
    {
        _isIpV6Supported ??= await IPAddressUtil.IsIpv6Supported();
        return _isIpV6Supported.Value;
    }

    private static Task<PingReply> SendPing(Ping? ping = null, IPAddress? ipAddress = null,
        TimeSpan? timeout = null)
    {
        timeout ??= TestConstants.DefaultPingTimeout;

        using var pingT = new Ping();
        ping ??= pingT;
        var buffer = new byte[1024];
        new Random().NextBytes(buffer);
        return ping.SendPingAsync(ipAddress ?? TestConstants.PingV4Address1, timeout.Value, buffer);
    }

    private async Task<bool> SendHttpGet(Uri uri, TimeSpan? timeout = null)
    {
        using var httpClient = new HttpClient(new HttpClientHandler {
            CheckCertificateRevocationList = false,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        });

        return await SendHttpGet(httpClient, uri, timeout);
    }

    private async Task<bool> SendHttpGet(HttpClient httpClient, Uri uri, TimeSpan? timeout)
    {
        timeout ??= TestConstants.DefaultHttpTimeout;
        var cancellationTokenSource = new CancellationTokenSource(timeout.Value);
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

        // fix TLS host; it may map by NetFilter.ProcessRequest
        if (IPEndPoint.TryParse(requestMessage.RequestUri!.Authority, out var ipEndPoint))
            requestMessage.Headers.Host = NetFilter.ProcessRequest(IpProtocol.Tcp, ipEndPoint)!.Address.ToString();

        var response = await httpClient.SendAsync(requestMessage, cancellationTokenSource.Token);
        var res = await response.Content.ReadAsStringAsync(cancellationTokenSource.Token);
        return res.Length > 100;
    }

    public async Task Test_Ping(Ping? ping = null, IPAddress? ipAddress = null, TimeSpan? timeout = null)
    {
        var pingReply = await SendPing(ping, ipAddress, timeout);
        if (pingReply.Status != IPStatus.Success)
            throw new PingException($"Ping failed. Status: {pingReply.Status}");
    }

    public async Task Test_Dns(IPEndPoint? nsEndPoint = null, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        timeout ??= TimeSpan.FromSeconds(3);
        var hostEntry = await DnsResolver.GetHostEntry("www.google.com", nsEndPoint ?? TestConstants.NsEndPoint1,
            timeout.Value, cancellationToken);
        Assert.IsNotNull(hostEntry);
        Assert.IsTrue(hostEntry.AddressList.Length > 0);
    }

    public Task Test_Udp(TimeSpan? timeout = null)
    {
        return Test_Udp(TestConstants.UdpV4EndPoint1, timeout);
    }

    public async Task Test_Udp(IPEndPoint udpEndPoint, TimeSpan? timeout = null)
    {
        if (udpEndPoint.IsV4()) {
            using var udpClientIpV4 = new UdpClient(AddressFamily.InterNetwork);
            await Test_Udp(udpClientIpV4, udpEndPoint, timeout);
        }

        else if (udpEndPoint.IsV6()) {
            using var udpClientIpV6 = new UdpClient(AddressFamily.InterNetworkV6);
            await Test_Udp(udpClientIpV6, udpEndPoint, timeout);
        }
    }

    public async Task Test_Udp(UdpClient udpClient, IPEndPoint udpEndPoint, TimeSpan? timeout = null)
    {
        timeout ??= TestConstants.DefaultUdpTimeout;

        var buffer = new byte[1024];
        new Random().NextBytes(buffer);

        var sentBytes = await udpClient.SendAsync(buffer, udpEndPoint, new CancellationTokenSource(timeout.Value).Token);
        Assert.AreEqual(buffer.Length, sentBytes);

        using var cts = new CancellationTokenSource(timeout.Value);
        var res = await udpClient.ReceiveAsync(cts.Token);
        CollectionAssert.AreEquivalent(buffer, res.Buffer);
    }

    public async Task Test_UdpByDNS(IPEndPoint udpEndPoint, TimeSpan? timeout = null)
    {
        timeout ??= TestConstants.DefaultUdpTimeout;
        var result = await DnsResolver.GetHostEntry("www.google.com", udpEndPoint, timeout.Value, CancellationToken.None);
        Assert.IsTrue(result.AddressList.Length > 0);
    }


    public async Task<bool> Test_Https(Uri? uri = null,
        TimeSpan? timeout = null, bool throwError = true)
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

    public IPAddress[] TestIpAddresses {
        get {
            var addresses = new List<IPAddress> {
                TestConstants.NsEndPoint1.Address,
                TestConstants.NsEndPoint2.Address,
                TestConstants.PingV4Address1,
                TestConstants.PingV4Address2,
                TestConstants.PingV6Address1,
                TestConstants.TcpEndPoint1.Address,
                TestConstants.TcpEndPoint2.Address,
                TestConstants.HttpsEndPoint1.Address,
                TestConstants.HttpsEndPoint1.Address,
                TestConstants.UdpV4EndPoint1.Address,
                TestConstants.UdpV4EndPoint2.Address,
                ClientOptions.Default.TcpProxyCatcherAddressIpV4,
                TestConstants.InvalidIp,
                TestConstants.BlockedIp
            };
            addresses.AddRange(Dns.GetHostAddresses(TestConstants.HttpsUri1.Host));
            addresses.AddRange(Dns.GetHostAddresses(TestConstants.HttpsUri2.Host));
            addresses.AddRange(Dns.GetHostAddresses(TestConstants.HttpsExternalUri1.Host));
            addresses.AddRange(Dns.GetHostAddresses(TestConstants.HttpsExternalUri2.Host));
            return addresses.ToArray();
        }
    }

    public Token CreateAccessToken(FileAccessManager fileAccessManager,
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

    private FileAccessManager GetFileAccessManagerFromServer(VpnHoodServer server)
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

    public Token CreateAccessToken(VpnHoodServer server,
        int maxClientCount = 1, int maxTrafficByteCount = 0, DateTime? expirationTime = null)
    {
        var fileAccessManager = GetFileAccessManagerFromServer(server);
        return CreateAccessToken(fileAccessManager, maxClientCount, maxTrafficByteCount, expirationTime);
    }

    public string CreateAccessManagerWorkingDir()
    {
        return Path.Combine(WorkingPath, $"AccessManager_{Guid.NewGuid()}");
    }

    public TestAccessManager CreateAccessManager(FileAccessManagerOptions? options = null,
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

    public FileAccessManagerOptions CreateFileAccessManagerOptions(IPEndPoint[]? tcpEndPoints = null)
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
                SyncCacheSize = 50
            },
            LogAnonymizer = false,
            UseExternalLocationService = false
        };
        return options;
    }

    public Task<VpnHoodServer> CreateServer(
        IAccessManager? accessManager = null,
        bool autoStart = true,
        TimeSpan? configureInterval = null,
        bool useHttpAccessManager = true,
        INetConfigurationProvider? netConfigurationProvider = null,
        ISwapMemoryProvider? swapMemoryProvider = null,
        ISocketFactory? socketFactory = null)
    {
        return CreateServer(accessManager, null,
            autoStart: autoStart,
            configureInterval: configureInterval,
            useHttpAccessManager: useHttpAccessManager,
            netConfigurationProvider: netConfigurationProvider,
            swapMemoryProvider: swapMemoryProvider,
            socketFactory: socketFactory);
    }

    public Task<VpnHoodServer> CreateServer(
        FileAccessManagerOptions? options,
        bool autoStart = true,
        TimeSpan? configureInterval = null,
        bool useHttpAccessManager = true,
        IVpnAdapter? vpnAdapter = null,
        ISocketFactory? socketFactory = null)
    {
        return CreateServer(
            accessManager: null,
            fileAccessManagerOptions: options,
            autoStart: autoStart,
            configureInterval: configureInterval,
            useHttpAccessManager: useHttpAccessManager,
            vpnAdapter: vpnAdapter,
            socketFactory: socketFactory);
    }

    private async Task<VpnHoodServer> CreateServer(IAccessManager? accessManager,
        FileAccessManagerOptions? fileAccessManagerOptions,
        bool autoStart, TimeSpan? configureInterval = null, bool useHttpAccessManager = true,
        INetConfigurationProvider? netConfigurationProvider = null,
        ISwapMemoryProvider? swapMemoryProvider = null,
        IVpnAdapter? vpnAdapter = null,
        ISocketFactory? socketFactory = null)
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
            SocketFactory = socketFactory ?? new TestSocketFactory(),
            ConfigureInterval = configureInterval ?? new ServerOptions().ConfigureInterval,
            AutoDisposeAccessManager = autoDisposeAccessManager,
            StoragePath = WorkingPath,
            NetFilter = NetFilter,
            NetConfigurationProvider = netConfigurationProvider,
            SwapMemoryProvider = swapMemoryProvider,
            VpnAdapter = vpnAdapter,
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

    public ISocketFactory CreateTestSocketFactory(IVpnAdapter? vpnAdapter = null)
    {
        return vpnAdapter != null
            ? new AdapterSocketFactory(vpnAdapter, new TestSocketFactory())
            : new TestSocketFactory();
    }

    public TestVpnAdapterOptions CreateTestVpnAdapterOptions()
    {
        return new TestVpnAdapterOptions();
    }

    public TestVpnAdapter CreateTestVpnAdapter(TestVpnAdapterOptions? options = null)
    {
        options ??= CreateTestVpnAdapterOptions();
        return new TestVpnAdapter(options);
    }


    public TestDevice CreateDevice(TestVpnAdapterOptions? testVpnAdapterOptions = null)
    {
        testVpnAdapterOptions ??= CreateTestVpnAdapterOptions();
        return new TestDevice(this, _ => new TestVpnAdapter(testVpnAdapterOptions));
    }

    public TestDevice CreateNullDevice(ITracker? tracker = null)
    {
        return new TestDevice(this, _ => new TestNullVpnAdapter());
    }

    public UdpProxyPoolOptions CreateUdpProxyPoolOptions(IPacketProxyCallbacks callbacks)
    {
        var proxyPool = new UdpProxyPoolOptions {
            PacketProxyCallbacks = callbacks,
            SocketFactory = new TestSocketFactory(),
            AutoDisposePackets = false,
            UdpTimeout = TunnelDefaults.UdpTimeout,
            MaxClientCount = 10,
            PacketQueueCapacity = TunnelDefaults.ProxyPacketQueueCapacity,
            BufferSize = null,
            LogScope = null
        };

        return proxyPool;
    }

    public TunnelOptions CreateTunnelOptions()
    {
        var tunnelOptions = new TunnelOptions {
            AutoDisposePackets = true,
            PacketQueueCapacity = TunnelDefaults.ProxyPacketQueueCapacity,
            MaxPacketChannelCount = TunnelDefaults.MaxPacketChannelCount
        };
        return tunnelOptions;
    }



    public ClientOptions CreateClientOptions(Token? token = null, bool useUdpChannel = false, string? clientId = null)
    {
        return new ClientOptions {
            AppName = "VpnHoodTester",
            SessionName = "UnitTestSession",
            ClientId = clientId ?? Guid.NewGuid().ToString(),
            AllowAnonymousTracker = true,
            AllowEndPointTracker = true,
            MaxPacketChannelCount = 1,
            UseUdpChannel = useUdpChannel,
            VpnAdapterIncludeIpRanges = TestIpAddresses.Select(IpRange.FromIpAddress).ToArray(),
            IncludeLocalNetwork = true,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            AccessKey = token?.ToAccessKey() ?? "" // set it later
        };
    }

    public Task<VpnHoodClient> CreateClient(Token token,
        IVpnAdapter? vpnAdapter = null,
        string? clientId = null,
        bool autoConnect = true)
    {
        var clientOptions = CreateClientOptions(token, clientId: clientId);
        return CreateClient(clientOptions, vpnAdapter, autoConnect);
    }

    public async Task<VpnHoodClient> CreateClient(
        ClientOptions clientOptions,
        IVpnAdapter? vpnAdapter = null,
        bool autoConnect = true)
    {
        vpnAdapter ??= new TestVpnAdapter(new TestVpnAdapterOptions());
        var client = new VpnHoodClient(vpnAdapter, new TestSocketFactory(), new TestTracker(), clientOptions);

        // test starting the client
        if (autoConnect)
            await client.Connect();

        return client;
    }


    public string GetParentDirectory(string path, int level = 1)
    {
        for (var i = 0; i < level; i++)
            path = Path.GetDirectoryName(path) ?? throw new Exception("Invalid path");

        return path;
    }

    public virtual void Dispose()
    {
        WebServer.Dispose();
        try {
            if (Directory.Exists(WorkingPath))
                Directory.Delete(WorkingPath, true);
        }
        catch {
            // ignored
        }
    }

    public static void AssemblyCleanup()
    {
        try {
            if (Directory.Exists(AssemblyWorkingPath))
                Directory.Delete(AssemblyWorkingPath, true);
        }
        catch {
            // ignored
        }
    }
}