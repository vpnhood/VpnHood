using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using System.Net;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server;
using VpnHood.Core.Server.Abstractions;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Server.Access.Managers.FileAccessManagement;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Proxies;
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
        public Task<bool> IsActive() => Task.FromResult(true);
        public Task<bool> IsDestroyed() => Task.FromResult(false);
    }

    public TestIps TestIps { get; } = new();
    public string WorkingPath { get; } = Path.Combine(AssemblyWorkingPath, Guid.CreateVersion7().ToString());
    public TestWebServer WebServer { get; }
    public NetFilter ClientNetFilter { get; }
    public NetFilter ServerNetFilter { get; }
    private bool? _isIpV6Supported;
    private int _accessTokenIndex;

    public TestHelper()
    {
        TunnelDefaults.TcpGracefulTimeout = TimeSpan.FromSeconds(10);
        VhLogger.Instance = VhLogger.CreateConsoleLogger(); // min level is controlled by VhLogger.MinLevel
        VhLogger.MinLogLevel = LogLevel.Debug;
        VhLogger.IsAnonymousMode = false;
        WebServer = TestWebServer.Create(TestIps);
        ClientNetFilter = new NetFilter {
            IpFilter = new StaticIpFilter(null) {
                BlockedRanges = new[] { WebServer.MockEps.HttpV4EndPointBlockedClient.Address }.ToOrderedIpRanges()
            },
            IpMapper = new TestIpMapper(TestIps)
        };
        ServerNetFilter = new NetFilter {
            IpFilter = new StaticIpFilter(null) {
                BlockedRanges = new[] { WebServer.MockEps.HttpV4EndPointBlockedServer.Address }.ToOrderedIpRanges()
            },
            IpMapper = new TestIpMapper(TestIps)
        };

        FastDateTime.Precision = TimeSpan.FromMilliseconds(1);
        JobOptions.DefaultInterval = TimeSpan.FromMilliseconds(1000);
        JobRunner.SlowInstance.Interval = TimeSpan.FromMilliseconds(200);
        JobRunner.FastInstance.Interval = TimeSpan.FromMilliseconds(200);
    }

    public async Task<bool> IsIpV6Supported()
    {
        _isIpV6Supported ??= await IPAddressUtil.IsIpv6Supported();
        return _isIpV6Supported.Value;
    }

    public Token CreateAccessToken(FileAccessManager fileAccessManager,
        int maxClientCount = 1, int maxTrafficByteCount = 0, DateTime? expirationTime = null, Traffic? maxSpeedMbps = null)
    {
        var accessToken = fileAccessManager.AccessTokenService.Create(
            tokenName: $"Test Server {++_accessTokenIndex}",
            maxClientCount: maxClientCount,
            maxTrafficByteCount: maxTrafficByteCount,
            expirationTime: expirationTime,
            maxSpeedMbps: maxSpeedMbps
        );

        return fileAccessManager.GetToken(accessToken);
    }

    private static FileAccessManager GetFileAccessManagerFromServer(VpnHoodServer server)
    {
        var accessManager = server.AccessManager;
        return accessManager switch {
            FileAccessManager fileAccessManager => fileAccessManager,
            TestHttpAccessManager {
                HttpAccessManagerServer.BaseAccessManager: FileAccessManager fileAccessManager
            } => fileAccessManager,
            _ => throw new InvalidOperationException("Could not get FileAccessManager from the server.")
        };
    }

    public Token CreateAccessToken(VpnHoodServer server,
        int maxClientCount = 1, int maxTrafficByteCount = 0, DateTime? expirationTime = null, 
        Traffic? maxSpeedMbps = null)
    {
        var fileAccessManager = GetFileAccessManagerFromServer(server);
        return CreateAccessToken(fileAccessManager, maxClientCount, maxTrafficByteCount, expirationTime, maxSpeedMbps);
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

    public FileAccessManagerOptions CreateFileAccessManagerOptions()
    {
        var options = new FileAccessManagerOptions {
            IsUnitTest = true,
            PublicEndPoints = null, // use TcpEndPoints
            TcpEndPoints = [VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback)],
            QuicEndPoints = [VhUtils.GetFreeUdpEndPoint(IPAddress.Loopback)],
            UdpEndPoints = [new IPEndPoint(IPAddress.Loopback, 0)],
            TrackingOptions = new TrackingOptions {
                TrackClientIp = true,
                TrackDestinationIp = true,
                TrackDestinationPort = true,
                TrackLocalPort = true
            },
            NetFilterOptions = new NetFilterOptions {
                IncludeLocalNetwork = false 
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
        ISocketFactory? socketFactory = null,
        CancellationToken cancellationToken = default)
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
            NetFilter = ServerNetFilter,
            NetConfigurationProvider = netConfigurationProvider,
            SwapMemoryProvider = swapMemoryProvider,
            VpnAdapter = vpnAdapter,
            PublicIpDiscovery = false //it slows down our tests
        };

        // Create server
        var server = new VpnHoodServer(accessManager, serverOptions);
        if (autoStart) {
            await server.Start(cancellationToken);
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
            MaxPacketChannelCount = TunnelDefaults.MaxPacketChannelCount,
            Mtu = TunnelDefaults.MtuClient
        };
        return tunnelOptions;
    }


    public ClientOptions CreateClientOptions(Token? token = null,
        ChannelProtocol channelProtocol = ChannelProtocol.Tcp,
        string? clientId = null,
        bool useTcpProxy = true)
    {
        return new ClientOptions {
            AppName = "VpnHoodTester",
            SessionName = "UnitTestSession",
            ClientId = clientId ?? Guid.NewGuid().ToString(),
            AllowAnonymousTracker = true,
            AllowEndPointTracker = true,
            MaxPacketChannelCount = 1,
            IncludeIpRangesByDevice = TestIps.AllRemoteTestIps.ToIpRanges().ToArray(),
            SplitLocalNetwork = true,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            ChannelProtocol = channelProtocol,
            UseTcpProxy = useTcpProxy,
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
        var client = new VpnHoodClient(vpnAdapter,
            socketFactory: new TestSocketFactory(),
            netFilter: ClientNetFilter,
            storageFolder: Path.Combine(WorkingPath, "ClientCore"),
            new TestTracker(), 
            clientOptions);

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