using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Core;
using Windows.Networking.Vpn;

namespace VpnHood.Core.Client.VpnServices.Host.WindowsVpnPlugin;

/// <summary>
/// Represents the VPN plug-in Windows calls, authored here for its callable wrapper; the VPN service does the work.
/// </summary>
/// <remarks>The process has one, which the VPN task's runs and the app's connections share.</remarks>
public sealed class VpnPluginBridge : IVpnPlugIn
{
    private const string PropertyKey = "WindowsVpnPlugin.Bridge";
    private static readonly Lock CurrentLock = new();
    private readonly WindowsVpnPluginService _service = new();

    // the process's one bridge, which the VPN task's runs and the app's connections share
    internal static VpnPluginBridge Current {
        get {
            lock (CurrentLock) {
                var properties = (IDictionary<string, object>)CoreApplication.Properties;
                if (properties.TryGetValue(PropertyKey, out var value) && value is VpnPluginBridge bridge)
                    return bridge;

                bridge = new VpnPluginBridge();
                properties[PropertyKey] = bridge;
                return bridge;
            }
        }
    }

    internal Task ServeAppConnection(AppServiceConnection connection, CancellationToken cancellationToken) =>
        _service.ServeAppConnection(connection, cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// It connects with the options the app saved, and returns once the adapter has started on the channel; a failure
    /// ends the channel's connection, with the failure's message.
    /// </remarks>
    public void Connect(VpnChannel channel) => _service.Connect(channel);

    /// <inheritdoc/>
    /// <remarks>
    /// The adapter and the channel stop at once; the session's close goes on after it returns, and the VPN task's run
    /// waits up to 15 seconds for it.
    /// </remarks>
    public void Disconnect(VpnChannel channel) => _service.Disconnect(channel);

    // the VPN task's run, after Windows' callback
    internal void WaitForDisconnect() => _service.WaitForDisconnect();

    /// <inheritdoc/>
    /// <param name="channel">The VPN channel of the connection.</param>
    /// <param name="packets">The device's IP packets that Windows routes into the VPN.</param>
    /// <param name="encapsulatedPackets">
    /// The list of packets for Windows to send on the channel's own transport, to which it adds none.
    /// </param>
    /// <remarks>
    /// The adapter takes the packets into the VPN; without one, they stay where Windows takes them back. A failure is
    /// not thrown, as it would end the VPN host: the first one tries to end the channel's connection instead.
    /// </remarks>
    public void Encapsulate(VpnChannel channel, VpnPacketBufferList packets, VpnPacketBufferList encapsulatedPackets) =>
        _service.Encapsulate(channel, packets, encapsulatedPackets);

    /// <inheritdoc/>
    /// <param name="channel">The VPN channel of the connection.</param>
    /// <param name="encapsulatedBuffer">The buffer that came on the channel's own transport, which it ignores.</param>
    /// <param name="decapsulatedPackets">The list of IP packets for Windows to hand the device, to which it adds none.</param>
    /// <param name="controlPacketsToSend">
    /// The list of control packets for Windows to send back on the channel's own transport, to which it adds none.
    /// </param>
    /// <remarks>
    /// It does nothing: the channel's own transport carries no VPN traffic, as the packets from the VPN reach the
    /// device through the channel's receive buffers.
    /// </remarks>
    public void Decapsulate(VpnChannel channel, VpnPacketBuffer encapsulatedBuffer,
        VpnPacketBufferList decapsulatedPackets, VpnPacketBufferList controlPacketsToSend) =>
        _service.Decapsulate(channel, encapsulatedBuffer, decapsulatedPackets, controlPacketsToSend);

    /// <inheritdoc/>
    /// <remarks>
    /// It gives no keep-alive packet: it sets <paramref name="keepAlivePacket"/> to <see langword="null"/>, which the
    /// projection's out parameter cannot declare.
    /// </remarks>
    public void GetKeepAlivePayload(VpnChannel channel, out VpnPacketBuffer keepAlivePacket)
    {
#nullable disable
        // ReSharper disable once AssignNullToNotNullAttribute
        keepAlivePacket = null;
#nullable restore
    }
}
