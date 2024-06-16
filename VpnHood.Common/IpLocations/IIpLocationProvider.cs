using System.Net;

namespace VpnHood.Common.IpLocations;

public interface IIpLocationProvider
{
    Task<IpLocation> GetLocation(HttpClient httpClient, IPAddress ipAddress, CancellationToken cancellationToken);
    Task<IpLocation> GetLocation(HttpClient httpClient, CancellationToken cancellationToken);
}