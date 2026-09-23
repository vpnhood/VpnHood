using System.Net;

namespace VpnHood.Core.Tunneling.Connections;

public class StreamConnectionDecorator(IStreamConnection streamConnection, Stream? stream = null) 
    : IStreamConnection
{
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

    public virtual void Dispose()
    {
        if (Disposed) return;
        Disposed = true;

        stream?.Dispose();
        InnerStreamConnection.Dispose();
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (Disposed) return;
        Disposed = true;

        if (stream != null) 
            await stream.DisposeAsync();

        await InnerStreamConnection.DisposeAsync();
    }
}