namespace VpnHood.Net.IpLocations;

public interface ICurrentIpLocationProvider : IDisposable
{
    Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken);
}