using System.Net;

namespace VpnHood.Core.IpLocations;

public interface IIpLocationProvider : IDisposable
{
    Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken);
    Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken);
}