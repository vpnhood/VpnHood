using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Server.Exceptions;
using VpnHood.Server.SystemInformation;
using VpnHood.Tunneling.Factory;
using Timer = System.Threading.Timer;

namespace VpnHood.Server
{
    public class VpnHoodServer : IDisposable
    {
        private readonly bool _autoDisposeAccessServer;
        private readonly Timer _updateStatusTimer;
        private readonly System.Timers.Timer _configureTimer;
        private readonly SocketFactory _socketFactory;
        private readonly TcpHost _tcpHost;
        private bool _disposed;

        public VpnHoodServer(IAccessServer accessServer, ServerOptions options)
        {
            _autoDisposeAccessServer = options.AutoDisposeAccessServer;
            _socketFactory = options.SocketFactory ?? throw new ArgumentNullException(nameof(options.SocketFactory));
            AccessServer = accessServer;
            SystemInfoProvider = options.SystemInfoProvider ?? new BasicSystemInfoProvider();
            SessionManager = new SessionManager(accessServer, options.SocketFactory, options.Tracker, options.AccessSyncCacheSize)
            {
                MaxDatagramChannelCount = options.MaxDatagramChannelCount
            };
            _tcpHost = new TcpHost(
                SessionManager,
                new SslCertificateManager(AccessServer),
                _socketFactory)
            {
                OrgStreamReadBufferSize = options.OrgStreamReadBufferSize,
                TunnelStreamReadBufferSize = options.TunnelStreamReadBufferSize
            };

            // Configure thread pool size
            ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
            ThreadPool.SetMinThreads(workerThreads, completionPortThreads * 30);

            // update timers
            _updateStatusTimer = new Timer(StatusTimerCallback, null, options.UpdateStatusInterval, options.UpdateStatusInterval);
            _configureTimer = new System.Timers.Timer(options.ConfigureInterval.TotalMilliseconds) {AutoReset = false};
            _configureTimer.Elapsed += OnConfigureTimerOnElapsed;
        }

        public SessionManager SessionManager { get; }
        public ServerState State { get; private set; } = ServerState.NotStarted;
        public IAccessServer AccessServer { get; }
        public ISystemInfoProvider SystemInfoProvider { get; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            using var _ = VhLogger.Instance.BeginScope("Server");
            VhLogger.Instance.LogInformation("Shutting down...");
            _updateStatusTimer.Dispose();
            _configureTimer.Dispose();

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

        private async void OnConfigureTimerOnElapsed(object o, ElapsedEventArgs elapsedEventArgs)
        {
            await Configure();
        }

        private async void StatusTimerCallback(object x)
        {
            await SendStatusToAccessServer();
        }

        /// <summary>
        ///     Start the server
        /// </summary>
        public async Task Start()
        {
            using var scope = VhLogger.Instance.BeginScope("Server");
            if (_disposed) throw new ObjectDisposedException(nameof(VpnHoodServer));

            if (State != ServerState.NotStarted)
                throw new Exception("Server has already started!");

            State = ServerState.Starting;

            // report config
            ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
            VhLogger.Instance.LogInformation(
                $"MinWorkerThreads: {workerThreads}, CompletionPortThreads: {completionPortThreads}, " +
                $"{nameof(TcpHost.OrgStreamReadBufferSize)}: {_tcpHost.OrgStreamReadBufferSize}, {nameof(TcpHost.TunnelStreamReadBufferSize)}: {_tcpHost.TunnelStreamReadBufferSize}");

            // Configure
            State = ServerState.Configuring;
            await Configure();
        }

        private async Task Configure()
        {
            if (State == ServerState.Started)
                throw new InvalidOperationException($"Could not {nameof(Configure)} when server state is started!");

            if (_tcpHost.IsDisposed)
                throw new ObjectDisposedException($"Could not {nameof(Configure)} after disposing {nameof(TcpHost)}!");

            try
            {
                // get server info
                VhLogger.Instance.LogTrace("Configuring from the Access Server...");
                var serverInfo = new ServerInfo(
                    environmentVersion: Environment.Version,
                    version: typeof(VpnHoodServer).Assembly.GetName().Version,
                    privateIpAddresses: await IPAddressUtil.GetPrivateIpAddresses(),
                    publicIpAddresses: await IPAddressUtil.GetPublicIpAddresses(),
                    status: Status
                    )
                {
                    MachineName = Environment.MachineName,
                    OsInfo = SystemInfoProvider.GetOperatingSystemInfo(),
                    TotalMemory = SystemInfoProvider.GetSystemInfo().TotalMemory,
                };

                // get configuration from access server
                var serverConfig = await AccessServer.Server_Configure(serverInfo);
                
                // configure
                VhLogger.Instance.LogTrace($"Starting {VhLogger.FormatTypeName<TcpHost>()}...");
                 _ =_tcpHost.Start(serverConfig.IPEndPoints);

                State = ServerState.Started;
                _configureTimer.Dispose();
                VhLogger.Instance.LogInformation("Server is ready!");
            }
            catch (MaintenanceException ex)
            {
                _configureTimer.Start();
                VhLogger.Instance.LogError(
                    $"Could not {nameof(Configure)} from the Access Server! Retrying after {_configureTimer.Interval / 1000} seconds. Message: {ex.Message}");
            }
        }

        public ServerStatus Status
        {
            get
            {
                var systemInfo = SystemInfoProvider.GetSystemInfo();
                var serverStatus = new ServerStatus
                {
                    SessionCount = SessionManager.Sessions.Count(x => !x.Value.IsDisposed),
                    TcpConnectionCount = SessionManager.Sessions.Values.Sum(x => x.TcpConnectionCount),
                    UdpConnectionCount = SessionManager.Sessions.Values.Sum(x => x.UdpConnectionCount),
                    ThreadCount = Process.GetCurrentProcess().Threads.Count,
                    FreeMemory = systemInfo.FreeMemory,
                    UsedMemory = Process.GetCurrentProcess().WorkingSet64
                };
                return serverStatus;
            }
        }

        private async Task SendStatusToAccessServer()
        {
            if (State != ServerState.Started)
                return;

            try
            {
                await AccessServer.Server_UpdateStatus(Status);
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, "Could not send server status!");
            }
        }
    }
}