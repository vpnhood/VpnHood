using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Client.Device;

public static class DeviceExtensions
{
    extension(IDevice device)
    {
        public bool TryBindProcessToVpn(bool value)
        {

            try {
                if (!device.IsBindProcessToVpnSupported)
                    VhLogger.Instance.LogError("BindProcessToVpn is not supported.");

                device.BindProcessToVpn(value);
                return true;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Could not bind process to VPN.");
                return false;
            }
        }

        public async Task<bool> TryBindProcessToVpn(bool value, 
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
}