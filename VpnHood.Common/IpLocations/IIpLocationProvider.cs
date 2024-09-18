using System.Net;

namespace VpnHood.Common.IpLocations;

public interface IIpLocationProvider
{
    Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken);
    Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken);
}
