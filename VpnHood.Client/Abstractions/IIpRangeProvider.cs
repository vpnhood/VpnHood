using System.Net;
using VpnHood.Common.Net;

namespace VpnHood.Client.Abstractions;

public interface IIpRangeProvider
{
    Task<IpRangeOrderedList?> GetIncludeIpRanges(IPAddress clientIp, CancellationToken cancellationToken);
}