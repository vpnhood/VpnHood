using System.Net;
using VpnHood.Common.Net;

namespace VpnHood.Client.Abstractions;

public interface IIpRangeProvider
{
    Task<IpRange[]?> GetIncludeIpRanges(IPAddress clientIp);
}