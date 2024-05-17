using System.Net;

namespace VpnHood.AccessServer.Agent.IpLocations;

public interface IIpLocationService
{
    Task<IpLocation> GetLocation(IPAddress ipAddress);
}