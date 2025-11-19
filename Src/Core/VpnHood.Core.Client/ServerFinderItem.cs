using System.Net;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.Core.Client;

public record ServerFinderItem2(ServerToken ServerToken, IPEndPoint IpEndPoint);

public record ServerFinderItem(IPEndPoint TcpEndPoint, string HostName, byte[]? CertificateHash);