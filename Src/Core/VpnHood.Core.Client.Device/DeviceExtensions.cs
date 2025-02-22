using Microsoft.Extensions.Logging;
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
}