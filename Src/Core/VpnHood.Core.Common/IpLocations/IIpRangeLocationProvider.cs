using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Common.IpLocations;

public interface IIpRangeLocationProvider : IIpLocationProvider
{
    Task<IpRangeOrderedList> GetIpRanges(string countryCode);
}