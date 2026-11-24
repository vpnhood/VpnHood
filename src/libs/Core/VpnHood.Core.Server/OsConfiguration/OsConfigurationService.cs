using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Server.Access;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Server.OsConfiguration;

// What the server changes on its machine's OS, when, and what a failure means; the provider only reads and changes, the
// restorer keeps the OS's own values, and ListenerIpAddresses the addresses the server added. A setting the machine
// lacks or refuses is left, logged, told to the access manager in the status, and the next configuration tries it again
internal class OsConfigurationService(
    IOsConfigurationProvider osConfigurationProvider,
    string storagePath,
    bool isPrivilegedProcess) : IDisposable
{
    private readonly OsSettingRestorer _osSettingRestorer = new(osConfigurationProvider, storagePath);
    private readonly ListenerIpAddresses _listenerIpAddresses = new(osConfigurationProvider);
    private readonly ConcurrentQueue<string> _errors = new();
    private bool _isUnprivilegedLogged;

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

    // read at each status, as an admin may change the OS after the server
    public async Task<OsStatus> GetOsStatus(CancellationToken cancellationToken)
    {
        var tcpCongestionControl = await TryGetTcpCongestionControl(cancellationToken).Vhc();
        var connectionTrackingMax = await TryGetConnectionTrackingMax(cancellationToken).Vhc();
        var connectionTrackingTcpIdleTimeout = await TryGetConnectionTrackingTcpIdleTimeout(cancellationToken).Vhc();
        return new OsStatus {
            TcpCongestionControl = tcpCongestionControl,
            ConnectionTrackingMax = connectionTrackingMax,
            ConnectionTrackingTcpIdleTimeout = connectionTrackingTcpIdleTimeout,
            Errors = _errors.ToArray()
        };
    }

    private async Task<string?> TryGetTcpCongestionControl(CancellationToken cancellationToken)
    {
        try {
            return await osConfigurationProvider.GetTcpCongestionControl(cancellationToken).Vhc();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            LogReadFailure(nameof(OsStatus.TcpCongestionControl), ex);
            return null;
        }
    }

    private async Task<int?> TryGetConnectionTrackingMax(CancellationToken cancellationToken)
    {
        try {
            return await osConfigurationProvider.GetConnectionTrackingMax(cancellationToken).Vhc();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            LogReadFailure(nameof(OsStatus.ConnectionTrackingMax), ex);
            return null;
        }
    }

    private async Task<TimeSpan?> TryGetConnectionTrackingTcpIdleTimeout(CancellationToken cancellationToken)
    {
        try {
            return await osConfigurationProvider.GetConnectionTrackingTcpIdleTimeout(cancellationToken).Vhc();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            LogReadFailure(nameof(OsStatus.ConnectionTrackingTcpIdleTimeout), ex);
            return null;
        }
    }

    // at each status, so a failure is not logged as a warning each time; a machine that lacks a setting is no failure
    private static void LogReadFailure(string setting, Exception ex)
    {
        if (ex is not NotSupportedException)
            VhLogger.Instance.LogDebug(ex, "Could not read {Setting}.", setting);
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
        _errors.Clear();

        // without admin rights each change would fail
        if (!isPrivilegedProcess) {
            if (!_isUnprivilegedLogged)
                VhLogger.Instance.LogInformation(
                    "The OS's settings are left as they are, as the server runs without admin rights.");

            _isUnprivilegedLogged = true;
            _errors.Enqueue("The server runs without admin rights, so it changes none of the OS's settings.");
            return;
        }

        foreach (var ipAddress in listenerIpAddresses) {
            try {
                await _listenerIpAddresses.Add(ipAddress, osOptions.AddListenerIpsToNetwork, cancellationToken).Vhc();
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
                ReportFailure($"{nameof(OsOptions.AddListenerIpsToNetwork)}: {ipAddress}", ex);
            }
        }

        // none, or *, puts back the OS's own, as 0 does for the numbers
        var tcpCongestionControl = osOptions.TcpCongestionControlValue;
        try {
            await _osSettingRestorer.SetTcpCongestionControl(
                string.IsNullOrEmpty(tcpCongestionControl) || tcpCongestionControl == "*" ? null : tcpCongestionControl,
                cancellationToken).Vhc();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            ReportFailure(nameof(OsOptions.TcpCongestionControl), ex);
        }

        // the timeout first: a network namespace of its own, as a container's, takes it but not the max
        var tcpIdleTimeout = osOptions.ConnectionTrackingTcpIdleTimeoutValue;
        try {
            await _osSettingRestorer.SetConnectionTrackingTcpIdleTimeout(
                tcpIdleTimeout == TimeSpan.Zero ? null : tcpIdleTimeout, cancellationToken).Vhc();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            ReportFailure(nameof(OsOptions.ConnectionTrackingTcpIdleTimeout), ex);
        }

        var max = osOptions.ConnectionTrackingMaxValue;
        try {
            await _osSettingRestorer.SetConnectionTrackingMax(max == 0 ? null : max, cancellationToken).Vhc();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            ReportFailure(nameof(OsOptions.ConnectionTrackingMax), ex);
        }

        await ConfigureSwapMemory(osOptions.SwapMemorySizeMb, cancellationToken).Vhc();
    }

    // a setting the machine lacks, or a value that is not valid, is no failure of the server's
    private void ReportFailure(string setting, Exception ex)
    {
        if (ex is NotSupportedException)
            VhLogger.Instance.LogInformation("{Setting} is left as it is, as this machine does not support it. {Message}",
                setting, ex.Message);
        else if (ex is ArgumentException)
            VhLogger.Instance.LogWarning("{Setting} is left as it is, as its value is not valid. {Message}",
                setting, ex.Message);
        else
            VhLogger.Instance.LogWarning(ex, "Could not set {Setting}.", setting);

        _errors.Enqueue($"{setting}: {ex.Message}");
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
        catch (Exception ex) when (ex is not OperationCanceledException) {
            ReportFailure(nameof(OsOptions.SwapMemorySizeMb), ex);
        }
    }

    public void Dispose() => _listenerIpAddresses.Dispose();
}
