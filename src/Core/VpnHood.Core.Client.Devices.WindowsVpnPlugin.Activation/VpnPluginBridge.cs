using Windows.Networking.Vpn;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin.Activation;

// The IVpnPlugIn Windows calls, authored here for its callable wrapper; the VPN service does the work.
public sealed class VpnPluginBridge : IVpnPlugIn
{
    private readonly WindowsVpnPluginService _service = new();

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
