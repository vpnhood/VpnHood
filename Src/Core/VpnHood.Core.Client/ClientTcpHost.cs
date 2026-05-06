using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Packets;
using VpnHood.Core.TcpStack;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;

internal class ClientTcpHost(ClientStreamHandler streamHandler)
    : IClientTcpHost
{
    private bool _disposed;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private LocalTcpStack? _tcpStack;
    private LocalTcpListener? _listener;

    public IReadOnlyList<IPAddress> CatcherAddressIps => [];
    public bool IsOwnPacket(IpPacket ipPacket) => false;

    public event EventHandler<IpPacket>? PacketReceived;


    public void DropCurrentConnections()
    {
        _tcpStack?.DropAllConnections();
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var logScope = VhLogger.Instance.BeginScope("ClientTcpHost");
        VhLogger.Instance.LogInformation("Starting ClientTcpHost (LocalTcpStack)...");
        _tcpStack = new LocalTcpStack {
            OnPacketSend = packet => PacketReceived?.Invoke(this, packet)
        };
        _listener = _tcpStack.ListenAny();
        Task.Run(() => AcceptLoop(_listener, _cancellationTokenSource.Token));
    }

    private async Task AcceptLoop(LocalTcpListener listener, CancellationToken cancellationToken)
    {
        try {
            await foreach (var tcpClient in listener.AcceptAllAsync(cancellationToken).ConfigureAwait(false)) {
                _ = ProcessAcceptedStream(tcpClient, cancellationToken);
            }
        }
        catch (OperationCanceledException) {
            // expected on dispose
        }
        catch (Exception ex) {
            if (!_disposed)
                VhLogger.LogError(GeneralEventId.Request, ex, "ClientHost accept loop terminated unexpectedly.");
        }
        finally {
            VhLogger.Instance.LogInformation("ClientHost accept loop has been closed.");
        }
    }

    private async Task ProcessAcceptedStream(LocalTcpClient localTcpClient, CancellationToken cancellationToken)
    {
        var connection = new LocalStreamConnection(localTcpClient);
        try {
            // The "host" endpoint is the original TCP destination captured by the stack.
            var hostEndPoint = localTcpClient.LocalEndPoint;
            await streamHandler.ProcessConnection(connection, hostEndPoint, cancellationToken).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(GeneralEventId.Stream, ex, "Could not process a tcp stream request.");
            await connection.DisposeAsync();
        }
    }

    // WARNING: Performance Critical!
    // Caller transfers ownership of ipPacket to this method.
    public void ProcessOutgoingPacket(IpPacket ipPacket)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_tcpStack == null)
            throw new InvalidOperationException("ClientHost has not been started.");

        // ProcessIncoming(IpPacket) does not take ownership; we must dispose ourselves.
        // on exception, the packet will be disposed by caller, so we don't need to worry about it here.
        _tcpStack.ProcessIncoming(ipPacket);
        ipPacket.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _cancellationTokenSource.TryCancel();
        _cancellationTokenSource.Dispose();

        _listener?.Dispose();
        _tcpStack?.Dispose();
        PacketReceived = null;
    }

    private sealed class LocalStreamConnection(LocalTcpClient tcpClient) : IConnection
    {
        private bool _disposed;

        public string ConnectionId { get; set; } = UniqueIdFactory.Create();
        public string ConnectionName => "app";
        public bool IsServer => false;
        public bool Connected => !_disposed && tcpClient.Stream is { CanRead: true, CanWrite: true };
        public Stream Stream => tcpClient.Stream;
        public IPEndPoint LocalEndPoint { get; } = tcpClient.LocalEndPoint;
        public IPEndPoint RemoteEndPoint { get; } = tcpClient.RemoteEndPoint;
        public bool RequireHttpResponse { get; set; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            tcpClient.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await tcpClient.DisposeAsync().Vhc();
        }
    }

}