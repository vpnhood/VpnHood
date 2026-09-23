namespace VpnHood.Core.IpLocations;

public interface ICurrentIpLocationProvider : IDisposable
{
    Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken);
}