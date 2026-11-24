using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Server.OsConfiguration;

// Adds the listeners' addresses the OS lacks, and removes the ones it added on close. A failed add throws, for the
// service to say; a failed removal is only logged, as a close goes on
internal class ListenerIpAddresses(IOsConfigurationProvider osConfigurationProvider) : IDisposable
{
    private readonly ConcurrentDictionary<IPAddress, string> _ipAddresses = [];
    private readonly Lock _closeLock = new();
    private bool _disposed;

    public async Task Add(IPAddress ipAddress, string? interfaceName, CancellationToken cancellationToken)
    {
        // a listener on any address needs none added, so no interface is looked up for it: a
        // container has no tool to find one
        if (ipAddress.Equals(IPAddress.Any) || ipAddress.Equals(IPAddress.IPv6Any))
            return;

        // find default interface name
        if (interfaceName == "*" || string.IsNullOrEmpty(interfaceName)) {
            var interfaceNames = await osConfigurationProvider.GetInterfaceNames(cancellationToken).Vhc();
            interfaceName = interfaceNames.FirstOrDefault() ??
                            throw new Exception("Could not find the default network interface name.");
        }

        // remove already added ip address if it belongs to different interface
        if (_ipAddresses.TryGetValue(ipAddress, out var oldInterfaceName)) {
            if (oldInterfaceName == interfaceName)
                return;

            Remove(ipAddress);
        }

        // add new ip address if it does not exist in the system
        if (await osConfigurationProvider.IpAddressExists(ipAddress, cancellationToken).Vhc())
            return;

        VhLogger.Instance.LogInformation("Adding IP address to system. IP: {IP}, InterfaceName: {interfaceName}",
            ipAddress, interfaceName);

        // not under the token: a cancel ends only the wait for the command, which still adds the address, and an
        // address added but not tracked would stay on the system
        await osConfigurationProvider.AddIpAddress(ipAddress, interfaceName, CancellationToken.None).Vhc();

        // removed at once if a close came meanwhile: it removed only what it found
        if (!Track(ipAddress, interfaceName))
            Remove(ipAddress);
    }

    // false once closed
    private bool Track(IPAddress ipAddress, string interfaceName)
    {
        lock (_closeLock) {
            _ipAddresses.TryAdd(ipAddress, interfaceName);
            return !_disposed;
        }
    }

    private void Remove(IPAddress ipAddress)
    {
        // taken out first, as both of the server's closes may run at once; put back if it stays on the system
        if (!_ipAddresses.TryRemove(ipAddress, out var interfaceName))
            return;

        try {
            VhLogger.Instance.LogInformation(
                "Removing IP address from system. IP: {IP}, InterfaceName: {interfaceName}",
                ipAddress, interfaceName);

            osConfigurationProvider.RemoveIpAddress(ipAddress, interfaceName);
        }
        catch (Exception ex) {
            _ipAddresses.TryAdd(ipAddress, interfaceName);
            VhLogger.Instance.LogWarning(ex,
                "Could not remove IP address from system. IP: {IP}, InterfaceName: {interfaceName}",
                ipAddress, interfaceName);
        }
    }

    // sync only: the removals are local commands, which the server's quick close runs too. An add that ends after
    // this removes its address itself
    public void Dispose()
    {
        lock (_closeLock)
            _disposed = true;

        foreach (var ipAddress in _ipAddresses.Keys.ToArray())
            Remove(ipAddress);
    }
}
