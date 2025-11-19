using System.Net;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.Core.Client;

public record ServerFinderItem(ServerToken ServerToken, IPEndPoint IpEndPoint);