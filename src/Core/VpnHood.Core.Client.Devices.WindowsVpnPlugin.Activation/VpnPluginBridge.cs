using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Core;
using Windows.Networking.Vpn;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin.Activation;

// The IVpnPlugIn Windows calls, authored here for its callable wrapper; the VPN service does the work.
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

    public void Connect(VpnChannel channel) => _service.Connect(channel);

    public void Disconnect(VpnChannel channel) => _service.Disconnect(channel);

    public void Encapsulate(VpnChannel channel, VpnPacketBufferList packets, VpnPacketBufferList encapsulatedPackets) =>
        _service.Encapsulate(channel, packets, encapsulatedPackets);

    public void Decapsulate(VpnChannel channel, VpnPacketBuffer encapsulatedBuffer,
        VpnPacketBufferList decapsulatedPackets, VpnPacketBufferList controlPacketsToSend) =>
        _service.Decapsulate(channel, encapsulatedBuffer, decapsulatedPackets, controlPacketsToSend);

    // No keep-alive: Windows reads null as none, which the projection's out parameter cannot declare.
    public void GetKeepAlivePayload(VpnChannel channel, out VpnPacketBuffer keepAlivePacket)
    {
#nullable disable
        keepAlivePacket = null;
#nullable restore
    }
}
