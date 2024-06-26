using System.Net;
using VpnHood.Common.IpLocations;

namespace VpnHood.AccessServer.Test.Helper;

public class TestIpLocationProvider : IIpLocationProvider
{
    public Task<IpLocation> GetLocation(HttpClient httpClient, IPAddress ipAddress, CancellationToken cancellationToken)
    {
        return Task.FromResult(new IpLocation
        {
            IpAddress = ipAddress,
            CountryName = ipAddress.GetAddressBytes()[0].ToString(),
            CountryCode = ipAddress.GetAddressBytes()[0].ToString(),
            RegionName = ipAddress.GetAddressBytes()[1].ToString(),
            RegionCode = ipAddress.GetAddressBytes()[1].ToString(),
            CityName = ipAddress.GetAddressBytes()[2].ToString(),
            CityCode = ipAddress.GetAddressBytes()[2].ToString(),
            ContinentCode = null
        });
    }

    public Task<IpLocation> GetLocation(HttpClient httpClient, CancellationToken cancellationToken)
    {
        return GetLocation(httpClient, IPAddress.Parse("127.0.0.1"), cancellationToken);
    }
}