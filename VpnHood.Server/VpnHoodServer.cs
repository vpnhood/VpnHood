using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Logging;
using VpnHood.Common;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Server.SystemInformation;
using Timer = System.Threading.Timer;

namespace VpnHood.Server
{

    public class VpnHoodServer : IDisposable
    {
        private readonly bool _autoDisposeAccessServer;
        private readonly System.Timers.Timer _configureTimer;
        private readonly TcpHost _tcpHost;
        private readonly string _lastConfigFilePath;
        private Timer? _updateStatusTimer;
        private bool _disposed;

        public VpnHoodServer(IAccessServer accessServer, ServerOptions options)
        {
            if (options.SocketFactory == null) throw new ArgumentNullException(nameof(options.SocketFactory));
            _autoDisposeAccessServer = options.AutoDisposeAccessServer;
            _lastConfigFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnHoodServer", "lastConfig.json");
            AccessServer = accessServer;
            SystemInfoProvider = options.SystemInfoProvider ?? new BasicSystemInfoProvider();
            SessionManager = new SessionManager(accessServer, options.SocketFactory, options.Tracker);
            _tcpHost = new TcpHost(SessionManager, new SslCertificateManager(AccessServer), options.SocketFactory);

            // Configure thread pool size
            ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
            ThreadPool.SetMinThreads(workerThreads, completionPortThreads * 30);
            
            ThreadPool.GetMaxThreads(out var workerThreadsMax, out var completionPortThreadsMax);
            ThreadPool.SetMaxThreads(workerThreadsMax, 0xFFFF); // We prefer all IO get slow than be queued

            // update timers
            _configureTimer = new System.Timers.Timer(options.ConfigureInterval.TotalMilliseconds) { AutoReset = false };
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

            using var scope = VhLogger.Instance.BeginScope("Server");
            VhLogger.Instance.LogInformation("Shutting down...");
            _updateStatusTimer?.Dispose();
            _configureTimer.Dispose();

            VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatTypeName(_tcpHost)}...");
            _tcpHost.Dispose();

            VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatTypeName(SessionManager)}...");
            SessionManager.Dispose();

            if (_autoDisposeAccessServer)
            {
                VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatTypeName(AccessServer)}...");
                AccessServer.Dispose();
            }

            State = ServerState.Disposed;
            VhLogger.Instance.LogInformation("Bye Bye!");
        }

        private async void OnConfigureTimerOnElapsed(object o, ElapsedEventArgs elapsedEventArgs)
        {
            await Configure();
        }

        private async void StatusTimerCallback(object state)
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
            ThreadPool.GetMinThreads(out var minWorkerThreads, out var minCompletionPortThreads);
            ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxCompletionPortThreads);
            VhLogger.Instance.LogInformation(
                $"MinWorkerThreads: {minWorkerThreads}, MinCompletionPortThreads: {minCompletionPortThreads}, " +
                $"MaxWorkerThreads: {maxWorkerThreads}, MaxCompletionPortThreads: {maxCompletionPortThreads}, ");

            // Configure
            State = ServerState.Configuring;
            await Configure();
        }

        private async Task Configure(Guid? configurationCode = null)
        {
            if (_tcpHost.IsDisposed)
                throw new ObjectDisposedException($"Could not configure after disposing {nameof(TcpHost)}!");

            try
            {
                // get server info
                VhLogger.Instance.LogInformation("Configuring by the Access Server...");
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
                    OsVersion = Environment.OSVersion.ToString(),
                    TotalMemory = SystemInfoProvider.GetSystemInfo().TotalMemory,
                    ConfigCode = configurationCode
                };

                // get configuration from access server
                var serverConfig = await ReadConfig(serverInfo);
                VhLogger.Instance.LogInformation($"ServerConfig: {JsonSerializer.Serialize(serverConfig)}");
                SessionManager.TrackingOptions = serverConfig.TrackingOptions;
                SessionManager.SessionOptions = serverConfig.SessionOptions;
                _tcpHost.OrgStreamReadBufferSize = serverConfig.SessionOptions.TcpBufferSize;
                _tcpHost.TunnelStreamReadBufferSize = serverConfig.SessionOptions.TcpBufferSize;
                _tcpHost.TcpTimeout = serverConfig.SessionOptions.TcpTimeout;

                // starting the listeners
                var verb = _tcpHost.IsStarted ? "Starting" : "Restarting";
                VhLogger.Instance.LogInformation($"{verb} {VhLogger.FormatTypeName(_tcpHost)}...");
                if (_tcpHost.IsStarted) await _tcpHost.Stop();
                _tcpHost.Start(serverConfig.TcpEndPoints);

                State = ServerState.Started;
                _configureTimer.Stop();

                // set _updateStatusTimer
                if (serverConfig.UpdateStatusInterval != TimeSpan.Zero)
                {
                    VhLogger.Instance.LogInformation($"Set {nameof(serverConfig.UpdateStatusInterval)} to {serverConfig.UpdateStatusInterval} seconds.");
                    _updateStatusTimer?.Dispose();
                    _updateStatusTimer = new Timer(StatusTimerCallback, null, serverConfig.UpdateStatusInterval, serverConfig.UpdateStatusInterval);
                }

                VhLogger.Instance.LogInformation("Server is ready!");
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError($"Could not configure server! Retrying after {_configureTimer.Interval / 1000} seconds. Message: {ex.Message}");
                await _tcpHost.Stop();
                _configureTimer.Start();
            }
        }

        private async Task<ServerConfig> ReadConfig(ServerInfo serverInfo)
        {
            try
            {
                var serverConfig = await AccessServer.Server_Configure(serverInfo);
                try { await File.WriteAllTextAsync(_lastConfigFilePath, JsonSerializer.Serialize(serverConfig)); }
                catch { /* Ignore */ }
                return serverConfig;
            }
            catch (MaintenanceException)
            {
                // try to load last config
                try
                {
                    if (File.Exists(_lastConfigFilePath))
                    {
                        var ret = Util.JsonDeserialize<ServerConfig>(await File.ReadAllTextAsync(_lastConfigFilePath));
                        VhLogger.Instance.LogWarning("Last configuration has been loaded to report Maintenance mode!");
                        return ret;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not load last {nameof(ServerConfig)}! Error: {ex.Message}");
                }

                throw;
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
                    UsedMemory = Process.GetCurrentProcess().WorkingSet64,
                    TunnelSendSpeed = SessionManager.Sessions.Sum(x => x.Value.Tunnel.SendSpeed),
                    TunnelReceiveSpeed = SessionManager.Sessions.Sum(x => x.Value.Tunnel.ReceiveSpeed)
                };
                return serverStatus;
            }
        }

        private async Task SendStatusToAccessServer()
        {
            Guid? configurationCode = null;
            try
            {
                VhLogger.Instance.LogTrace("Sending status to AccessServer!");
                var res = await AccessServer.Server_UpdateStatus(Status);
                configurationCode = res.ConfigCode;
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, "Could not send server status!");
            }

            // reconfigure
            if (configurationCode != null)
            {
                VhLogger.Instance.LogInformation("Reconfiguration was requested.");
                _ = Configure(configurationCode);
            }
        }
    }
}