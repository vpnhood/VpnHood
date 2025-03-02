using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Server.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Server;

public class NetConfigurationService(INetConfigurationProvider netConfigurationProvider) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<IPAddress, string> _ipAddresses = [];

    public async Task<string[]?> GetNetworkInterfaceNames()
    {
        try {
            return await netConfigurationProvider.GetInterfaceNames();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not find the default network interface name.");
            return null;
        }
    }

    private Task<bool> IpAddressExists(IPAddress ipAddress)
    {
        if (ipAddress.Equals(IPAddress.Any) ||
            ipAddress.Equals(IPAddress.IPv6Any))
            return Task.FromResult(true);

        return netConfigurationProvider.IpAddressExists(ipAddress);
    }

    public async Task AddIpAddress(IPAddress ipAddress, string? interfaceName)
    {
        try {
            // find default interface name
            if (interfaceName == "*" || string.IsNullOrEmpty(interfaceName))
                interfaceName = (await netConfigurationProvider.GetInterfaceNames()).FirstOrDefault() ??
                                throw new Exception("Could not find the default network interface name.");

            // remove already added ip address if it belongs to different interface
            if (_ipAddresses.TryGetValue(ipAddress, out var oldInterfaceName)) {
                if (oldInterfaceName == interfaceName)
                    return;

                await RemoveIpAddress(ipAddress);
            }

            // add new ip address if it does not exist in the system
            if (await IpAddressExists(ipAddress).VhConfigureAwait())
                return;

            VhLogger.Instance.LogInformation("Adding IP address to system. IP: {IP}, InterfaceName: {interfaceName}",
                ipAddress, interfaceName);

            await netConfigurationProvider.AddIpAddress(ipAddress, interfaceName).VhConfigureAwait();
            _ipAddresses.TryAdd(ipAddress, interfaceName);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex,
                "Could not add IP address to system. IP: {IP}, InterfaceName: {interfaceName}",
                ipAddress, interfaceName);
        }
    }

    private async Task RemoveIpAddress(IPAddress ipAddress)
    {
        if (!_ipAddresses.TryGetValue(ipAddress, out var interfaceName)) {
            VhLogger.Instance.LogWarning("IP address has not been added by NetConfigurationService. IP: {IP}",
                ipAddress);
            return;
        }

        try {
            VhLogger.Instance.LogInformation(
                "Removing IP address from system. IP: {IP}, InterfaceName: {interfaceName}",
                ipAddress, interfaceName);

            await netConfigurationProvider.RemoveIpAddress(ipAddress, interfaceName).VhConfigureAwait();
            _ipAddresses.TryRemove(ipAddress, out _);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex,
                "Could not remove IP address from system. IP: {IP}, InterfaceName: {interfaceName}",
                ipAddress, interfaceName);
        }
    }

    public async Task<string?> GetTcpCongestionControl()
    {
        try {
            return await netConfigurationProvider.GetTcpCongestionControl();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could no read TCP congestion control.");
            return null;
        }
    }

    public async Task SetTcpCongestionControl(string value)
    {
        try {
            var tcpCongestionControl = await GetTcpCongestionControl();
            if (value == tcpCongestionControl || value == "*" || tcpCongestionControl == null)
                return;

            await netConfigurationProvider.SetTcpCongestionControl(value);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could no set TCP congestion control.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var ipAddress in _ipAddresses.ToArray())
            await RemoveIpAddress(ipAddress.Key);
    }
}