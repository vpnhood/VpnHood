using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Server.OsConfiguration;

// What the server changes on its machine's OS, when, and what a failure means; the provider only reads and changes, and
// the restorer keeps the OS's own values. A setting the machine lacks or refuses is left, logged, and the next
// configuration tries it again
internal class OsConfigurationService(
    IOsConfigurationProvider osConfigurationProvider,
    string storagePath,
    bool isPrivilegedProcess) : IDisposable
{
    private readonly OsSettingRestorer _osSettingRestorer = new(osConfigurationProvider, storagePath);
    private readonly ConcurrentDictionary<IPAddress, string> _ipAddresses = [];
    private readonly Lock _closeLock = new();
    private bool _isUnprivilegedLogged;
    private bool _disposed;

    public async Task<IReadOnlyList<string>?> GetNetworkInterfaceNames(CancellationToken cancellationToken)
    {
        try {
            return await osConfigurationProvider.GetInterfaceNames(cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (NotSupportedException) {
            return null;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not list the network interfaces.");
            return null;
        }
    }

    public async Task<string?> GetTcpCongestionControl(CancellationToken cancellationToken)
    {
        try {
            return await osConfigurationProvider.GetTcpCongestionControl(cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (NotSupportedException) {
            return null;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not read TCP congestion control.");
            return null;
        }
    }

    // at each status, so a failure is not logged as a warning each time
    public async Task<SwapMemoryInfo?> GetSwapMemoryInfo(CancellationToken cancellationToken)
    {
        try {
            return await osConfigurationProvider.GetSwapMemoryInfo(cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (NotSupportedException) {
            return null;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not read the swap memory.");
            return null;
        }
    }

    public async Task Configure(OsOptions osOptions, IReadOnlyList<IPAddress> listenerIpAddresses,
        CancellationToken cancellationToken)
    {
        // without admin rights each change would fail
        if (!isPrivilegedProcess) {
            if (!_isUnprivilegedLogged)
                VhLogger.Instance.LogInformation(
                    "The OS's settings are left as they are, as the server runs without admin rights.");

            _isUnprivilegedLogged = true;
            return;
        }

        foreach (var ipAddress in listenerIpAddresses)
            await AddIpAddress(ipAddress, osOptions.AddListenerIpsToNetwork, cancellationToken).Vhc();

        // none, or *, puts back the OS's own, as 0 does for the numbers
        var tcpCongestionControl = osOptions.TcpCongestionControlValue;
        await _osSettingRestorer.SetTcpCongestionControl(
            string.IsNullOrEmpty(tcpCongestionControl) || tcpCongestionControl == "*" ? null : tcpCongestionControl,
            cancellationToken).Vhc();

        // the timeout first: a network namespace of its own, as a container's, takes it but not the max
        var tcpIdleTimeout = osOptions.ConnectionTrackingTcpIdleTimeoutValue;
        await _osSettingRestorer.SetConnectionTrackingTcpIdleTimeout(
            tcpIdleTimeout == TimeSpan.Zero ? null : tcpIdleTimeout, cancellationToken).Vhc();

        var max = osOptions.ConnectionTrackingMaxValue;
        await _osSettingRestorer.SetConnectionTrackingMax(max == 0 ? null : max, cancellationToken).Vhc();

        await ConfigureSwapMemory(osOptions.SwapMemorySizeMb, cancellationToken).Vhc();
    }

    private async Task ConfigureSwapMemory(long? sizeMb, CancellationToken cancellationToken)
    {
        try {
            var info = await osConfigurationProvider.GetSwapMemoryInfo(cancellationToken).Vhc();
            var size = sizeMb * VhUtils.Megabytes ?? 0;
            var otherSize = info.TotalSize - info.AppSize;
            var newAppSize = Math.Max(0, size - otherSize);

            if (Math.Abs(info.AppSize - newAppSize) < VhUtils.Megabytes) {
                if (size == 0 && info.AppSize == 0)
                    return; // there is no need to configure swap file

                VhLogger.Instance.LogInformation(
                    "Swap file size is already near to the requested size. CurrentSize: {CurrentSize}, RequestedSize: {RequestedSize}",
                    VhUtils.FormatBytes(info.TotalSize), VhUtils.FormatBytes(size));
                return;
            }

            VhLogger.Instance.LogInformation("Updating the server's swap file. Size: {Size}",
                VhUtils.FormatBytes(newAppSize));
            await osConfigurationProvider.SetAppSwapMemorySize(newAppSize, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (NotSupportedException ex) {
            VhLogger.Instance.LogInformation("The swap file is left as it is, as this machine does not support it. {Message}",
                ex.Message);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not configure swap file.");
        }
    }

    public async Task AddIpAddress(IPAddress ipAddress, string? interfaceName, CancellationToken cancellationToken)
    {
        // a listener on any address needs none added, so no interface is looked up for it: a
        // container has no tool to find one
        if (ipAddress.Equals(IPAddress.Any) || ipAddress.Equals(IPAddress.IPv6Any))
            return;

        try {
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

                RemoveIpAddress(ipAddress);
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
            if (!TrackIpAddress(ipAddress, interfaceName))
                RemoveIpAddress(ipAddress);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (NotSupportedException ex) {
            VhLogger.Instance.LogInformation(
                "The IP address is not added, as this machine does not support it. IP: {IP}, {Message}",
                ipAddress, ex.Message);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex,
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
            RemoveIpAddress(ipAddress);
    }
}
