using VpnHood.Core.VpnAdapters.WinDivert;

namespace VpnHood.Test.Device;

public class TestVpnAdapter(TestVpnAdapterOptions vpnAdapterOptions)
    : WinDivertVpnAdapter(new WinDivertVpnAdapterSettings {
        AdapterName = "VpnHoodTestAdapter"
    })
{
    public TestVpnAdapterOptions VpnAdapterOptions { get; } = vpnAdapterOptions;
}
