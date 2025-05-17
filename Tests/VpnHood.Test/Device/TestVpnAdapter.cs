using VpnHood.Core.VpnAdapters.WinDivert;

namespace VpnHood.Test.Device;

public class TestVpnAdapter(TestVpnAdapterOptions vpnAdapterOptions)
    : WinDivertVpnAdapter(new WinDivertVpnAdapterSettings {
        AdapterName = "VpnHoodTestAdapter",
        Blocking = true, // lets simulate client
        AutoDisposePackets = true
    })
{
    public TestVpnAdapterOptions VpnAdapterOptions { get; } = vpnAdapterOptions;
}
