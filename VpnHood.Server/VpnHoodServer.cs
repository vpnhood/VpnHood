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

        public SessionManager SessionManager { get; }
        public ServerState State { get; private set; } = ServerState.NotStarted;
        public IPEndPoint TcpHostEndPoint => _tcpHost.LocalEndPoint;
        public IAccessServer AccessServer { get; }

        public bool IsDiagnoseMode { get; }
        public string ServerId { get; private set; }

        public VpnHoodServer(IAccessServer accessServer, ServerOptions options)
        {
            if (options.SocketFactory == null) throw new ArgumentNullException(nameof(options.SocketFactory));
            IsDiagnoseMode = options.IsDiagnoseMode;
            ServerId = options.ServerId ?? LoadServerId();
            SessionManager = new SessionManager(accessServer, options.SocketFactory, options.Tracker, ServerId);
            AccessServer = accessServer;
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
            ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
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
            ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
            VhLogger.Instance.LogInformation($"MinWorkerThreads: {workerThreads}, CompletionPortThreads: {completionPortThreads}");

            // Starting hosts
            VhLogger.Instance.LogTrace($"Starting {VhLogger.FormatTypeName<TcpHost>()}...");
            _tcpHost.Start();

            State = ServerState.Started;
            VhLogger.Instance.LogInformation($"Server is ready!");

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
            var serverIdFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnHood.Server", "ServerId");
            if (!File.Exists(serverIdFile))
            {
                var random = new Random();
                var bytes = new byte[8];
                random.NextBytes(bytes);
                var newId = BitConverter.ToUInt64(bytes, 0).ToString();

                Directory.CreateDirectory(Path.GetDirectoryName(serverIdFile));
                File.WriteAllText(serverIdFile, newId);
            }

            return File.ReadAllText(serverIdFile);
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

            _reportTimer?.Dispose();
            State = ServerState.Disposed;
            VhLogger.Instance.LogInformation("Bye Bye!");
        }
    }
}

