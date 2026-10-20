using System.Net;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Test.Providers;

// a connection over the given stream, with made-up endpoints
public sealed class TestStreamConnection(Stream stream) : IStreamConnection
{
    public string ConnectionId { get; set; } = Guid.NewGuid().ToString();
    public string ConnectionName => "test";
    public bool IsServer => false;
    public bool Connected => stream is { CanRead: true, CanWrite: true };
    public Stream Stream => stream;
    public IPEndPoint LocalEndPoint { get; } = new(IPAddress.Loopback, 1);
    public IPEndPoint RemoteEndPoint { get; } = new(IPAddress.Loopback, 2);
    public bool RequireHttpResponse { get; set; }
    public void PreventReuse() { }
    public void Dispose() => stream.Dispose();
    public ValueTask DisposeAsync() => stream.DisposeAsync();
}
