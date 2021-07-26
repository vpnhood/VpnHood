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
        private readonly TcpHost _tcpHost;

        public SessionManager SessionManager { get; }
        public ServerState State { get; private set; } = ServerState.NotStarted;
        public IPEndPoint TcpHostEndPoint => _tcpHost.LocalEndPoint;
        public IAccessServer AccessServer { get; }
        public Guid ServerId { get; private set; }

        public VpnHoodServer(IAccessServer accessServer, ServerOptions options)
        {
            if (options.SocketFactory == null) throw new ArgumentNullException(nameof(options.SocketFactory));
            ServerId = options.ServerId ?? LoadServerId();
            AccessServer = accessServer;
            SessionManager = new SessionManager(accessServer, options.SocketFactory, options.Tracker, ServerId)
            {
                MaxDatagramChannelCount = options.MaxDatagramChannelCount
            };
            _tcpHost = new TcpHost(
                endPoint: options.TcpHostEndPoint,
                sessionManager: SessionManager,
                sslCertificateManager: new SslCertificateManager(accessServer, ServerId),
                socketFactory: options.SocketFactory)
            {
                OrgStreamReadBufferSize = options.OrgStreamReadBufferSize,
                TunnelStreamReadBufferSize = options.TunnelStreamReadBufferSize
            };

            // Configure thread pool size
            ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
            ThreadPool.SetMinThreads(workerThreads, completionPortThreads * 30);
        }

        /// <summary>
        ///  Start the server
        /// </summary>
        public Task Start()
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

            State = ServerState.Started;
            VhLogger.Instance.LogInformation($"Server is ready!");

            return Task.FromResult(0);
        }

        private static Guid LoadServerId()
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

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            using var _ = VhLogger.Instance.BeginScope("Server");
            VhLogger.Instance.LogInformation("Shutting down...");

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

