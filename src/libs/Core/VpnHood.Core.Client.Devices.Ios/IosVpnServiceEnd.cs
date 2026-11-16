using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using NetworkExtension;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.Ios;

// Why a VPN service the app took for running is gone. iOS ends the extension's process without a word to it when the
// app is updated - no StopTunnel - so its status file still says connected. iOS keeps how the session ended: an update
// leaves no disconnect error, a crash or a kill for memory leaves one (NEVPNConnectionErrorDomain 12, "an internal
// error"). A session that ended shows as Disconnected (VpnServiceNotReadyException); a failure keeps its dialog
// (VpnServiceUnreachableException). iOS 15 keeps no such error, so there every end is a session that ended.
internal static class IosVpnServiceEnd
{
    // iOS answers at once; without an answer the status poll waiting here must still go on
    private static readonly TimeSpan LastErrorTimeout = TimeSpan.FromSeconds(5);

    public static async Task<Exception> ToException(NETunnelProviderManager? manager, CancellationToken cancellationToken)
    {
        if (manager == null)
            return Ended("its VPN configuration is gone");

        if (!OperatingSystem.IsIOSVersionAtLeast(16) && !OperatingSystem.IsTvOSVersionAtLeast(17))
            return Ended("not recorded before iOS 16");

        try {
            var error = await GetLastDisconnectError(manager.Connection, cancellationToken).Vhc();
            return error == null
                ? Ended("no error")
                : new VpnServiceUnreachableException(
                    $"VpnService is unreachable. iOS has ended it. Error: {error.Domain} {error.Code}",
                    new Exception(error.LocalizedDescription));
        }
        catch (Exception ex) {
            return new VpnServiceUnreachableException("VpnService is unreachable. iOS did not say how it ended.", ex);
        }
    }

    [SupportedOSPlatform("ios16.0")]
    [SupportedOSPlatform("tvos17.0")]
    private static Task<NSError?> GetLastDisconnectError(NEVpnConnection connection, CancellationToken cancellationToken)
    {
        var errorTask = new TaskCompletionSource<NSError?>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.FetchLastDisconnectError(error => errorTask.TrySetResult(error));
        return errorTask.Task.WaitAsync(LastErrorTimeout, cancellationToken);
    }

    private static VpnServiceNotReadyException Ended(string reason)
    {
        VhLogger.Instance.LogInformation("iOS has ended the VPN service, and the session with it. Reason: {Reason}",
            reason);
        return new VpnServiceNotReadyException($"iOS has ended the VPN service. Reason: {reason}");
    }
}
