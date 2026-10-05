using System.Net;
using System.Net.Sockets;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;

namespace VpnHood.AppLib.Test.Daemon;

// A platform's channel, stood in for by a loopback TCP listener: the line protocol over it is the
// real one, and who the caller is comes from the test rather than from a pipe or a socket.
internal sealed class TestDaemonChannel : IDaemonChannel
{
    private TcpListener? _listener;

    // What the next callers are found to be.
    public bool IsAdministrator { get; set; } = true;
    public string CallerName { get; set; } = "tester";

    // The next accept fails, as one does on a system out of handles.
    public bool FailNextAccept { get; set; }

    public Task<IDaemonChannelListener> Listen(CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _listener = listener;
        return Task.FromResult<IDaemonChannelListener>(new Listener(this, listener));
    }

    public async Task<Stream> Connect(CancellationToken cancellationToken)
    {
        var listener = _listener ?? throw new InvalidOperationException("Nothing serves the channel.");
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try {
            await socket.ConnectAsync((IPEndPoint)listener.LocalEndpoint, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch {
            socket.Dispose();
            throw;
        }
    }

    private sealed class Listener(TestDaemonChannel channel, TcpListener listener) : IDaemonChannelListener
    {
        public async Task<IDaemonChannelCaller> Accept(CancellationToken cancellationToken)
        {
            var socket = await listener.AcceptSocketAsync(cancellationToken);
            if (channel.FailNextAccept) {
                channel.FailNextAccept = false;
                socket.Dispose();
                throw new IOException("Too many open files.");
            }

            return new Caller(channel, new NetworkStream(socket, ownsSocket: true));
        }

        public ValueTask DisposeAsync()
        {
            listener.Stop();
            channel._listener = null;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Caller(TestDaemonChannel channel, NetworkStream stream) : IDaemonChannelCaller
    {
        public Stream Stream => stream;
        public DaemonCallerInfo Identify() => new(channel.CallerName, channel.IsAdministrator);
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }
}
