using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.IpLocations;

public interface IIpRangeLocationProvider : IIpLocationProvider
{
    Task<string[]> GetCountryCodes(CancellationToken cancellationToken);
    Task<IpRangeOrderedList> GetIpRanges(string countryCode, CancellationToken cancellationToken);
}