using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.IpLocations;

public interface IIpRangeLocationProvider : IIpLocationProvider
{
    Task<string[]> GetCountryCodes(CancellationToken cancellationToken);
    Task<IpRangeOrderedList> GetIpRanges(string countryCode, CancellationToken cancellationToken);
}