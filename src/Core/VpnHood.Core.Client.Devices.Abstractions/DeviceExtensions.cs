using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.Abstractions;

/// <summary>
/// Provides helpers of a device.
/// </summary>
public static class DeviceExtensions
{
    /// <summary>Provides the tries of a device's binding of the app's connections.</summary>
    /// <param name="device">The device.</param>
    extension(IDevice device)
    {
        /// <summary>
        /// Calls <see cref="IDevice.BindProcessToVpn"/>, logging a failure rather than throwing it, and an error first
        /// where the device does not support it.
        /// </summary>
        /// <param name="value">
        /// <see langword="true"/> for the system's default network; <see langword="false"/> for a network outside the
        /// VPN.
        /// </param>
        /// <returns><see langword="true"/> if the call threw nothing; otherwise, <see langword="false"/>.</returns>
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

        /// <summary>
        /// Waits, then calls <see cref="IDevice.BindProcessToVpn"/> as <see cref="TryBindProcessToVpn(IDevice, bool)"/>
        /// does.
        /// </summary>
        /// <param name="value">
        /// <see langword="true"/> for the system's default network; <see langword="false"/> for a network outside the
        /// VPN.
        /// </param>
        /// <param name="delay">How long to wait first.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>
        /// A task whose result is <see langword="true"/> if the call threw nothing; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>A canceled or refused wait still makes the call, at once, and then throws.</remarks>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> is negative, other than -1 ms, or too long.</exception>
        public async Task<bool> TryBindProcessToVpn(bool value,
            TimeSpan delay, CancellationToken cancellationToken)
        {
            bool result;
            try {
                await Task.Delay(delay, cancellationToken).Vhc();
            }
            finally {
                result = device.TryBindProcessToVpn(value);
            }

            return result;
        }
    }
}