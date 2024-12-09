namespace VpnHood.Common.IpLocations;

public interface IRegionProvider
{
    string GetClientCountry();
    Task<string> GetCurrentCountryAsync(CancellationToken cancellationToken);
}