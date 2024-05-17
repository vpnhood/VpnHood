using System.Net;
using VpnHood.AccessServer.Agent.IpLocations;

namespace VpnHood.AccessServer.Test.Helper;

public class TestIpLocationService : IIpLocationService
{
    public Task<IpLocation> GetLocation(IPAddress ipAddress)
    {
        return Task.FromResult(new IpLocation
        {
            CountryName = ipAddress.GetAddressBytes()[0].ToString(),
            CountryCode = ipAddress.GetAddressBytes()[0].ToString(),
            RegionName = ipAddress.GetAddressBytes()[1].ToString(),
            RegionCode = ipAddress.GetAddressBytes()[1].ToString(),
            CityName = ipAddress.GetAddressBytes()[2].ToString(),
            CityCode = ipAddress.GetAddressBytes()[2].ToString(),
            ContinentCode = null
        });
    }
}