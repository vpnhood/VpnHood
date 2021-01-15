using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Logging;
using VpnHood.Tunneling;

namespace VpnHood.Server
{
    public class VpnHoodServer : IDisposable
    {
        private bool _disposed;
        private Timer _reportTimer;
        private readonly TcpHost _tcpHost;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        private ILogger _logger => VhLogger.Current;

        public SessionManager SessionManager { get; }
        public ServerState State { get; private set; } = ServerState.NotStarted;
        public IPEndPoint TcpHostEndPoint => _tcpHost.LocalEndPoint;
        public IAccessServer AccessServer { get; }

        public bool IsDiagnoseMode { get; }
        public string ServerId { get; private set; }

        public VpnHoodServer(IAccessServer accessServer, ServerOptions options)
        {
            if (options.TcpClientFactory == null) throw new ArgumentNullException(nameof(options.TcpClientFactory));
            if (options.UdpClientFactory == null) throw new ArgumentNullException(nameof(options.UdpClientFactory));
            IsDiagnoseMode = options.IsDiagnoseMode;
            ServerId = options.ServerId ?? LoadServerId();
            SessionManager = new SessionManager(accessServer, options.UdpClientFactory, options.Tracker, ServerId);
            AccessServer = accessServer;
            _tcpHost = new TcpHost(
                endPoint: options.TcpHostEndPoint,
                sessionManager: SessionManager,
                sslCertificateManager: new SslCertificateManager(accessServer),
                tcpClientFactory: options.TcpClientFactory);
        }

        /// <summary>
        ///  Start the server
        /// </summary>
        public Task Start()
        {
            using var _ = _logger.BeginScope("Server");
            if (_disposed) throw new ObjectDisposedException(nameof(VpnHoodServer));

            if (State != ServerState.NotStarted)
                throw new Exception("Server has already started!");

            State = ServerState.Starting;

            // Starting hosts
            _logger.LogTrace($"Starting {VhLogger.FormatTypeName<TcpHost>()}...");
            _tcpHost.Start();

            State = ServerState.Started;
            _logger.LogInformation("Server is ready!");

            if (IsDiagnoseMode)
                _reportTimer = new Timer(ReportStatus, null, 0, 5 * 60000);

            return Task.FromResult(0);
        }

        private void ReportStatus(object state)
        {
            SessionManager.ReportStatus();
        }

        private static string LoadServerId()
        {
            var serverIdFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnHood_ServerId");
            if (!File.Exists(serverIdFile))
                File.WriteAllText(serverIdFile, TunnelUtil.RandomLong().ToString());

            return File.ReadAllText(serverIdFile);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            using var _ = _logger.BeginScope("Server");
            _logger.LogInformation("Shutting down...");

            _logger.LogTrace($"Disposing {VhLogger.FormatTypeName<TcpHost>()}...");
            _tcpHost.Dispose();

            _logger.LogTrace($"Disposing {VhLogger.FormatTypeName<SessionManager>()}...");
            SessionManager.Dispose();

            _logger.LogTrace($"Disposing {VhLogger.FormatTypeName<Nat>()}...");

            _reportTimer?.Dispose();
            State = ServerState.Disposed;
            _logger.LogInformation("Bye Bye!");
        }
    }
}

