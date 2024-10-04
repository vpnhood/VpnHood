using System.Net;
using VpnHood.Common.IpLocations;

namespace VpnHood.AccessServer.Test.Helper;

public class TestIpLocationProvider : IIpLocationProvider
{
    public Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        var countryCode = ipAddress.GetAddressBytes()[0].ToString();
        countryCode = countryCode.Length >= 2 ? countryCode[..2] : countryCode[..1];

        return Task.FromResult(new IpLocation {
            IpAddress = ipAddress,
            CountryCode = countryCode,
            CountryName = ipAddress.GetAddressBytes()[0].ToString(),
            RegionName = ipAddress.GetAddressBytes()[1].ToString(),
            CityName = ipAddress.GetAddressBytes()[2].ToString(),
        });
    }

    public Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        return GetLocation(IPAddress.Parse("127.0.0.1"), cancellationToken);
    }
}