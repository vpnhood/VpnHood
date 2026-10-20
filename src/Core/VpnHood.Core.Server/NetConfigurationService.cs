using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Server.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Server;

public class NetConfigurationService(INetConfigurationProvider netConfigurationProvider) : IDisposable
{
    private readonly ConcurrentDictionary<IPAddress, string> _ipAddresses = [];
    private readonly Lock _closeLock = new();
    private bool _disposed;

    public async Task<string[]?> GetNetworkInterfaceNames()
    {
        try {
            return await netConfigurationProvider.GetInterfaceNames().Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not find the default network interface name.");
            return null;
        }
    }

    public async Task AddIpAddress(IPAddress ipAddress, string? interfaceName)
    {
        // a listener on any address needs none added, so no interface is looked up for it: a
        // container has no tool to find one
        if (ipAddress.Equals(IPAddress.Any) || ipAddress.Equals(IPAddress.IPv6Any))
            return;

        try {
            // find default interface name
            if (interfaceName == "*" || string.IsNullOrEmpty(interfaceName))
                interfaceName = (await netConfigurationProvider.GetInterfaceNames().Vhc()).FirstOrDefault() ??
                                throw new Exception("Could not find the default network interface name.");

            // remove already added ip address if it belongs to different interface
            if (_ipAddresses.TryGetValue(ipAddress, out var oldInterfaceName)) {
                if (oldInterfaceName == interfaceName)
                    return;

                RemoveIpAddress(ipAddress);
            }

            // add new ip address if it does not exist in the system
            if (await netConfigurationProvider.IpAddressExists(ipAddress).Vhc())
                return;

            VhLogger.Instance.LogInformation("Adding IP address to system. IP: {IP}, InterfaceName: {interfaceName}",
                ipAddress, interfaceName);

            await netConfigurationProvider.AddIpAddress(ipAddress, interfaceName).Vhc();

            // removed at once if a close came meanwhile: it removed only what it found
            if (!TrackIpAddress(ipAddress, interfaceName))
                RemoveIpAddress(ipAddress);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex,
                "Could not add IP address to system. IP: {IP}, InterfaceName: {interfaceName}",
                ipAddress, interfaceName);
        }
    }

    // false once closed
    private bool TrackIpAddress(IPAddress ipAddress, string interfaceName)
    {
        lock (_closeLock) {
            _ipAddresses.TryAdd(ipAddress, interfaceName);
            return !_disposed;
        }
    }

    private void RemoveIpAddress(IPAddress ipAddress)
    {
        // taken out first, as both of the server's closes may run at once; put back if it stays on the system
        if (!_ipAddresses.TryRemove(ipAddress, out var interfaceName))
            return;

        try {
            VhLogger.Instance.LogInformation(
                "Removing IP address from system. IP: {IP}, InterfaceName: {interfaceName}",
                ipAddress, interfaceName);

            netConfigurationProvider.RemoveIpAddress(ipAddress, interfaceName);
        }
        catch (Exception ex) {
            _ipAddresses.TryAdd(ipAddress, interfaceName);
            VhLogger.Instance.LogError(ex,
                "Could not remove IP address from system. IP: {IP}, InterfaceName: {interfaceName}",
                ipAddress, interfaceName);
        }
    }

    public async Task<string?> GetTcpCongestionControl()
    {
        try {
            return await netConfigurationProvider.GetTcpCongestionControl().Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not read TCP congestion control.");
            return null;
        }
    }

    public async Task SetTcpCongestionControl(string value)
    {
        try {
            var tcpCongestionControl = await GetTcpCongestionControl().Vhc();
            if (value == tcpCongestionControl || value == "*" || tcpCongestionControl == null)
                return;

            await netConfigurationProvider.SetTcpCongestionControl(value).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not set TCP congestion control.");
        }
    }

    // sync only: the removals are local commands, which the server's quick close runs too. An add that ends after
    // this removes its address itself
    public void Dispose()
    {
        lock (_closeLock)
            _disposed = true;

        foreach (var ipAddress in _ipAddresses.Keys.ToArray())
            RemoveIpAddress(ipAddress);
    }
}