using System.Net;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Tunneling.Connections;

public class StreamConnectionDecorator(IStreamConnection streamConnection, Stream? stream = null) 
    : IStreamConnection
{
    private readonly AsyncLock _disposeLock = new();
    protected bool Disposed;
    protected IStreamConnection InnerStreamConnection => streamConnection;

    public virtual bool Connected => !Disposed && InnerStreamConnection.Connected;
    public Stream Stream => stream ?? InnerStreamConnection.Stream;
    public IPEndPoint LocalEndPoint => InnerStreamConnection.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => InnerStreamConnection.RemoteEndPoint;
    public bool IsServer => InnerStreamConnection.IsServer;
    public string ConnectionName  => InnerStreamConnection.ConnectionName;

    public string ConnectionId {
        get => InnerStreamConnection.ConnectionId;
        set => InnerStreamConnection.ConnectionId = value;
    }

    public bool RequireHttpResponse {
        get => InnerStreamConnection.RequireHttpResponse;
        set => InnerStreamConnection.RequireHttpResponse = value;
    }

    public virtual void PreventReuse() => InnerStreamConnection.PreventReuse();

    public virtual void Dispose()
    {
        if (Interlocked.Exchange(ref Disposed, true))
            return;

        stream?.Dispose();
        InnerStreamConnection.Dispose();
    }

    public virtual async ValueTask DisposeAsync()
    {
        // a second close waits for the first one's end. No early return after it: the inner connection's async
        // close, which a quick close may have started without waiting, ends before this one does
        using var scopeLock = await _disposeLock.LockAsync().Vhc();
        if (stream != null)
            await stream.DisposeAsync().Vhc();

        await InnerStreamConnection.DisposeAsync().Vhc();
        Dispose();
    }
}