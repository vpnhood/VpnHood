using System.Net;

namespace VpnHood.Server.Abstractions;

public interface INetConfigurationProvider
{
    Task<string[]> GetInterfaceNames();
    Task AddIpAddress(IPAddress ipAddress, string interfaceName);
    Task RemoveIpAddress(IPAddress ipAddress, string interfaceName);
    Task<bool> IpAddressExists(IPAddress ipAddress);
}