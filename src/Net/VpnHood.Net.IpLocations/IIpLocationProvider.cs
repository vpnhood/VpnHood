using System.Net;

namespace VpnHood.Net.IpLocations;

public interface IIpLocationProvider : ICurrentIpLocationProvider
{
    Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken);
}