using Microsoft.Extensions.Logging;
using System.Threading;
using VpnHood.Core.Common.Logging;

namespace VpnHood.Core.Client.Device;

public static class DeviceExtensions
{
    public static bool TryBindProcessToVpn(this IDevice device, bool value)
    {

        try {
            if (!device.IsBindProcessToVpnSupported) {
                VhLogger.Instance.LogError("BindProcessToVpn is not supported.");
            }

            device.BindProcessToVpn(value);
            return true;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not bind process to VPN.");
            return false;
        }
    }

    public static async Task<bool> TryBindProcessToVpn(this IDevice device, bool value, 
        TimeSpan delay, CancellationToken cancellationToken)
    {
        bool result;
        try {
            await Task.Delay(delay, cancellationToken);
        }
        finally {
            result = device.TryBindProcessToVpn(true);
        }

        return result;
    }

}