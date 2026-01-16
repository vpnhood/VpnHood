using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.IpLocations;

public interface IIpRangeLocationProvider : IIpLocationProvider
{
    Task<IpRangeOrderedList> GetIpRanges(string countryCode);
}