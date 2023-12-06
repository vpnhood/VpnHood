using System.Net;
using VpnHood.Common.Net;

namespace VpnHood.Client;

public interface IIpRangeProvider
{
    Task<IpRange[]?> GetIncludeIpRanges(IPAddress clientIp);
}
