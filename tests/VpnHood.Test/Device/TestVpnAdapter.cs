using VpnHood.Net.VpnAdapters.WinDivert;

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

    // as the adapter says once the host moved to other networks
    public void SimulatePrimaryNetworkChange() => OnPrimaryNetworkChanged();
}