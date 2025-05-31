using VpnHood.Core.VpnAdapters.WinDivert;

namespace VpnHood.Test.Device;

public class TestVpnAdapter(TestVpnAdapterOptions vpnAdapterOptions)
    : WinDivertVpnAdapter(new WinDivertVpnAdapterSettings {
        AdapterName = "VpnHoodTestAdapter",
        Blocking = false,
        AutoDisposePackets = true,
        SimulateDns = vpnAdapterOptions.SimulateDns
    })
{
    public TestVpnAdapterOptions VpnAdapterOptions { get; } = vpnAdapterOptions;

}
