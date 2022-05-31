using System.Net;
using System.Threading.Tasks;
using VpnHood.Common.Net;

namespace VpnHood.Client;

public interface IIpFilter
{
    Task<IpRange[]?> GetIncludeIpRanges(IPAddress clientIp);
}
