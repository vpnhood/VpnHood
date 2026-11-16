using System.Net;

namespace VpnHood.Net.Http3;

/// <summary>
/// Provides the endpoints of the connection a stream belongs to.
/// </summary>
/// <param name="LocalEndPoint">The connection's local address and port.</param>
/// <param name="RemoteEndPoint">The connection's remote address and port.</param>
public sealed record Http3StreamContext(IPEndPoint LocalEndPoint, IPEndPoint RemoteEndPoint);
