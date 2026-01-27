using System.Net;

namespace VpnHood.Core.IpLocations;

public interface IIpLocationProvider : ICurrentIpLocationProvider
{
    Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken);
}