using System.Net;

namespace VpnHood.Common.IpLocations;

public interface IIpLocationProvider
{
    Task<IpLocation> GetLocation(HttpClient httpClient, IPAddress ipAddress);
    Task<IpLocation> GetLocation(HttpClient httpClient);
}