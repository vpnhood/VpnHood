namespace VpnHood.AppLib.Abstractions;

public interface IRegionProvider
{
    string GetClientCountryCode(bool allowVpnServer);
    Task<string> GetClientCountryCodeAsync(bool allowVpnServer, CancellationToken cancellationToken);
}