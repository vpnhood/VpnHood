using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using VpnHood.Logging;
using VpnHood.Tunneling;

namespace VpnHood.Server
{
    public class VpnHoodServer : IDisposable
    {
        private readonly TcpHost _tcpHost;
        public SessionManager SessionManager { get; }
        public ServerState State { get; private set; } = ServerState.NotStarted;
        public IPEndPoint TcpHostEndPoint => _tcpHost.LocalEndPoint;
        public IAccessServer AccessServer { get; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        private ILogger _logger => VhLogger.Current;
        public string ServerId { get; private set; }

        public VpnHoodServer(IAccessServer accessServer, ServerOptions options)
        {
            if (options.TcpClientFactory == null) throw new ArgumentNullException(nameof(options.TcpClientFactory));
            if (options.UdpClientFactory == null) throw new ArgumentNullException(nameof(options.UdpClientFactory));
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

            return Task.FromResult(0);
        }

        private static string LoadServerId()
        {
            var serverIdFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnHood_ServerId");
            if (!File.Exists(serverIdFile))
                File.WriteAllText(serverIdFile, TunnelUtil.RandomLong().ToString());
           
            return File.ReadAllText(serverIdFile);
        }

        private bool _disposed;
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

            State = ServerState.Disposed;
            _logger.LogInformation("Bye Bye!");
        }
    }
}

