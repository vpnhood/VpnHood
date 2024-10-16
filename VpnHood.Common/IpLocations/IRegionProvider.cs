namespace VpnHood.Common.IpLocations;

public interface IRegionProvider
{
    string GetCurrentCountryCode();
    Task<string> GetCurrentCountryCodeAsync(CancellationToken cancellationToken);
}