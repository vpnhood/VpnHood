using System.Net;

namespace VpnHood.Core.Tunneling.Connections;

public class ConnectionDecorator(IConnection connection, Stream? stream = null) 
    : IConnection
{
    protected bool _disposed;
    protected IConnection _innerConnection = connection;

    public virtual bool Connected => !_disposed && _innerConnection.Connected;
    public Stream Stream => stream ?? _innerConnection.Stream;
    public IPEndPoint LocalEndPoint => _innerConnection.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => _innerConnection.RemoteEndPoint;
    public bool IsServer => _innerConnection.IsServer;
    public string ConnectionName  => _innerConnection.ConnectionName;

    public string ConnectionId {
        get => _innerConnection.ConnectionId;
        set => _innerConnection.ConnectionId = value;
    }

    public bool RequireHttpResponse {
        get => _innerConnection.RequireHttpResponse;
        set => _innerConnection.RequireHttpResponse = value;
    }

    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        stream?.Dispose();
        _innerConnection.Dispose();
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (stream != null) 
            await stream.DisposeAsync();

        await _innerConnection.DisposeAsync();
    }
}