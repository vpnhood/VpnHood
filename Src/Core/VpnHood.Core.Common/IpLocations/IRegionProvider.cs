namespace VpnHood.Core.Common.IpLocations;

public interface IRegionProvider
{
    string GetClientCountryCode(bool allowVpnServer);
    Task<string> GetClientCountryCodeAsync(bool allowVpnServer, CancellationToken cancellationToken);
}