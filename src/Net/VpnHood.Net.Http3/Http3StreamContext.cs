using System.Net;

namespace VpnHood.Net.Http3;

public sealed record Http3StreamContext(IPEndPoint LocalEndPoint, IPEndPoint RemoteEndPoint);
