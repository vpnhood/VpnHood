namespace VpnHood.Core.Common.IpLocations;

public interface IRegionProvider
{
    string GetClientCountry();
    Task<string> GetCurrentCountryAsync(CancellationToken cancellationToken);
}