using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Common;
using VpnHood.Logging;
using VpnHood.Server.Exceptions;
using VpnHood.Server.SystemInformation;

namespace VpnHood.Server
{
    public class VpnHoodServer : IDisposable
    {
        private bool _disposed;
        private readonly TcpHost _tcpHost;
        private readonly Timer _sendStatusTimer;
        private readonly System.Timers.Timer _subscribeTimer;
        private readonly bool _autoDisposeAccessServer;

        public SessionManager SessionManager { get; }
        public ServerState State { get; private set; } = ServerState.NotStarted;
        public IPEndPoint TcpHostEndPoint => _tcpHost.LocalEndPoint;
        public IAccessServer AccessServer { get; }
        public Guid ServerId { get; private set; }
        public ISystemInfoProvider SystemInfoProvider { get; private set; }

        public VpnHoodServer(IAccessServer accessServer, ServerOptions options)
        {
            if (options.SocketFactory == null) throw new ArgumentNullException(nameof(options.SocketFactory));
            ServerId = options.ServerId ?? GetServerId();
            AccessServer = accessServer;
            _autoDisposeAccessServer = options.AutoDisposeAccessServer;
            SystemInfoProvider = options.SystemInfoProvider ?? new BasicSystemInfoProvider();
            SessionManager = new SessionManager(accessServer, options.SocketFactory, options.Tracker, options.AccessSyncCacheSize)
            {
                MaxDatagramChannelCount = options.MaxDatagramChannelCount
            };
            _tcpHost = new TcpHost(
                endPoint: options.TcpHostEndPoint,
                sessionManager: SessionManager,
                sslCertificateManager: new SslCertificateManager(accessServer),
                socketFactory: options.SocketFactory)
            {
                OrgStreamReadBufferSize = options.OrgStreamReadBufferSize,
                TunnelStreamReadBufferSize = options.TunnelStreamReadBufferSize
            };

            // Configure thread pool size
            ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
            ThreadPool.SetMinThreads(workerThreads, completionPortThreads * 30);

            // update timers
            _sendStatusTimer = new(async (x) => await SendStatusToAccessServer(), null, options.SendStatusInterval, options.SendStatusInterval);
            _subscribeTimer = new(options.SubscribeInterval.TotalMilliseconds) { AutoReset = false };
            _subscribeTimer.Elapsed += async (_, _) => await Subscribe();
        }

        /// <summary>
        ///  Start the server
        /// </summary>
        public async Task Start()
        {
            using var _ = VhLogger.Instance.BeginScope("Server");
            if (_disposed) throw new ObjectDisposedException(nameof(VpnHoodServer));

            if (State != ServerState.NotStarted)
                throw new Exception("Server has already started!");

            State = ServerState.Starting;

            // report config
            ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
            VhLogger.Instance.LogInformation($"MinWorkerThreads: {workerThreads}, CompletionPortThreads: {completionPortThreads}");

            // Starting hosts
            VhLogger.Instance.LogTrace($"Starting {VhLogger.FormatTypeName<TcpHost>()}...");
            _tcpHost.Start();

            // Subscribe
            State = ServerState.Subscribing;
            await Subscribe();
        }

        public static Guid GetServerId()
        {
            var serverIdFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnHood.Server", "ServerId");
            if (File.Exists(serverIdFile) && Guid.TryParse(File.ReadAllText(serverIdFile), out var serverId))
            {
                return serverId;
            }
            else
            {
                serverId = Guid.NewGuid();
                Directory.CreateDirectory(Path.GetDirectoryName(serverIdFile));
                File.WriteAllText(serverIdFile, serverId.ToString());
                return serverId;
            }
        }

        private async Task Subscribe()
        {
            try
            {
                // get server info
                VhLogger.Instance.LogTrace("Subscribing to the Access Server...");
                var serverInfo = new ServerInfo(typeof(VpnHoodServer).Assembly.GetName().Version)
                {
                    EnvironmentVersion = Environment.Version,
                    MachineName = Environment.MachineName,
                    OsInfo = SystemInfoProvider.GetOperatingSystemInfo(),
                    TotalMemory = SystemInfoProvider.GetSystemInfo().TotalMemory,
                    PublicIp = await Util.GetPublicIpAddress(),
                    LocalIp = await Util.GetLocalIpAddress(),
                };

                // finish subscribing
                await AccessServer.Server_Subscribe(serverInfo);
                State = ServerState.Started;
                _subscribeTimer.Dispose();
                VhLogger.Instance.LogInformation($"Server is ready!");

                // send status
                await SendStatusToAccessServer();
            }
            catch (MaintenanceException ex)
            {
                _subscribeTimer.Start();
                VhLogger.Instance.LogError($"Could not SubScribe to the Access Server! Retrying after {_subscribeTimer.Interval / 1000} seconds. Message: {ex.Message}");
            }
        }

        private async Task SendStatusToAccessServer()
        {
            if (State != ServerState.Started)
                return;

            try
            {
                var systemInfo = SystemInfoProvider.GetSystemInfo();
                var serverStatus = new ServerStatus
                {
                    SessionCount = SessionManager.Sessions.Count(x => !x.Value.IsDisposed),
                    TcpConnectionCount = SessionManager.Sessions.Values.Sum(x => x.TcpConnectionCount),
                    UdpConnectionCount = SessionManager.Sessions.Values.Sum(x => x.UdpConnectionCount),
                    ThreadCount = Process.GetCurrentProcess().Threads.Count,
                    FreeMemory = systemInfo.FreeMemory,
                    UsedMemory = Process.GetCurrentProcess().WorkingSet64,
                };

                await AccessServer.Server_SetStatus(serverStatus);
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, "Could not send server status!");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            using var _ = VhLogger.Instance.BeginScope("Server");
            VhLogger.Instance.LogInformation("Shutting down...");
            _sendStatusTimer.Dispose();
            _subscribeTimer.Dispose();

            VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatTypeName<TcpHost>()}...");
            _tcpHost.Dispose();

            VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatTypeName<SessionManager>()}...");
            SessionManager.Dispose();

            if (_autoDisposeAccessServer)
            {
                VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatTypeName<IAccessServer>()}...");
                AccessServer.Dispose();
            }

            State = ServerState.Disposed;
            VhLogger.Instance.LogInformation("Bye Bye!");
        }
    }
}

