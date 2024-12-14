using System.Net;

namespace VpnHood.Core.Server.Abstractions;

public interface INetConfigurationProvider
{
    Task<string[]> GetInterfaceNames();
    Task AddIpAddress(IPAddress ipAddress, string interfaceName);
    Task RemoveIpAddress(IPAddress ipAddress, string interfaceName);
    Task<bool> IpAddressExists(IPAddress ipAddress);
    Task<string> GetTcpCongestionControl();
    Task SetTcpCongestionControl(string value);
}