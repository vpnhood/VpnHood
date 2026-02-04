using System.Net;

namespace VpnHood.Core.Tunneling.Connections;

public class ConnectionDecorator(IConnection connection, Stream? stream = null) 
    : IConnection
{
    protected bool Disposed;
    protected IConnection InnerConnection = connection;

    public virtual bool Connected => !Disposed && InnerConnection.Connected;
    public Stream Stream => stream ?? InnerConnection.Stream;
    public IPEndPoint LocalEndPoint => InnerConnection.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => InnerConnection.RemoteEndPoint;
    public bool IsServer => InnerConnection.IsServer;
    public string ConnectionName  => InnerConnection.ConnectionName;

    public string ConnectionId {
        get => InnerConnection.ConnectionId;
        set => InnerConnection.ConnectionId = value;
    }

    public bool RequireHttpResponse {
        get => InnerConnection.RequireHttpResponse;
        set => InnerConnection.RequireHttpResponse = value;
    }

    public virtual void Dispose()
    {
        if (Disposed) return;
        Disposed = true;

        stream?.Dispose();
        InnerConnection.Dispose();
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (Disposed) return;
        Disposed = true;

        if (stream != null) 
            await stream.DisposeAsync();

        await InnerConnection.DisposeAsync();
    }
}