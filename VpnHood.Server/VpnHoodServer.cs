using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Threading.Tasks;

namespace VpnHood.Server
{
    public class VpnHoodServer : IDisposable
    {
        private readonly TcpHost _tcpHost;
        private ILogger Logger => Loggers.Logger.Current;
        public SessionManager SessionManager { get; }
        public ServerState State { get; private set; } = ServerState.NotStarted;
        public IPEndPoint TcpHostEndPoint => _tcpHost.LocalEndPoint;
        public VpnHoodServer(IClientStore tokenStore, ServerOptions options)
        {
            if (options.TcpClientFactory == null) throw new ArgumentNullException(nameof(options.TcpClientFactory));
            if (options.UdpClientFactory == null) throw new ArgumentNullException(nameof(options.UdpClientFactory));
            SessionManager = new SessionManager(tokenStore, options.UdpClientFactory);
            _tcpHost = new TcpHost(options.TcpHostEndPoint, options.Certificate, SessionManager, options.TcpClientFactory);
        }

        /// <summary>
        ///  Start the server
        /// </summary>
        public Task Start()
        {
            using var _ = Logger.BeginScope("Server");
            if (_disposed) throw new ObjectDisposedException(nameof(VpnHoodServer));

            if (State != ServerState.NotStarted)
                throw new Exception("Server has already started!");

            State = ServerState.Starting;

            // Starting hosts
            Logger.LogTrace($"Starting {Util.FormatTypeName<TcpHost>()}...");
            _tcpHost.Start();

            State = ServerState.Started;
            Logger.LogInformation("Server is ready!");

            return Task.FromResult(0);
        }

        private bool _disposed;
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            using var _ = Logger.BeginScope("Server");
            Logger.LogInformation("Shutting down...");

            Logger.LogTrace($"Disposing {Util.FormatTypeName<TcpHost>()}...");
            _tcpHost.Dispose();

            Logger.LogTrace($"Disposing {Util.FormatTypeName<SessionManager>()}...");
            SessionManager.Dispose();

            Logger.LogTrace($"Disposing {Util.FormatTypeName<Nat>()}...");

            State = ServerState.Disposed;
            Logger.LogInformation("Bye Bye!");
        }
    }
}

