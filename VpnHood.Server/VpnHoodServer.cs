using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Logging;
using VpnHood.Server.SystemInformation;
using VpnHood.Tunneling;

namespace VpnHood.Server
{
    public class VpnHoodServer : IDisposable
    {
        private bool _disposed;
        private readonly TcpHost _tcpHost;
        private readonly Timer _sendStatusTimer;
        private readonly System.Timers.Timer _subscribeTimer;

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
            SystemInfoProvider = options.SystemInfoProvider ?? new SimpleSystemInfoProvider();
            SessionManager = new SessionManager(accessServer, options.SocketFactory, options.Tracker)
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
            _sendStatusTimer = new (async (x) => await SendStatusToAccessServer(), null, options.SendStatusInterval, options.SendStatusInterval);
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

            VhLogger.Instance.LogInformation($"Server is ready!");
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
                VhLogger.Instance.LogTrace("Subscribing to the Access Server...");
                var serverInfo = new ServerInfo
                {
                    EnvironmentVersion = Environment.Version,
                    MachineName = Environment.MachineName,
                    Version = typeof(VpnHoodServer).Assembly.GetName().Version,
                    OsInfo = SystemInfoProvider?.GetOperatingSystemInfo(),
                    TotalMemory = SystemInfoProvider?.GetSystemInfo()?.TotalMemory ?? 0,
                };

                await AccessServer.SubscribeServer(serverInfo);
                State = ServerState.Started;
                _subscribeTimer.Dispose();

                await SendStatusToAccessServer();

            }
            catch (Exception ex)
            {
                _subscribeTimer.Start();
                VhLogger.Instance.LogError(ex, $"Could not SubScribe to the Access Server! Retry in {_subscribeTimer.Interval / 1000} seconds.");
            }
        }

        private async Task SendStatusToAccessServer()
        {
            if (State!= ServerState.Started)
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

                await AccessServer.SendServerStatus(serverStatus);
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

            VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatTypeName<Nat>()}...");

            State = ServerState.Disposed;
            VhLogger.Instance.LogInformation("Bye Bye!");
        }
    }
}

