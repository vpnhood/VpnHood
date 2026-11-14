using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.VpnAdapters.WinDivert;

namespace VpnHood.Test.Device;

public class TestVpnAdapter(TestVpnAdapterOptions vpnAdapterOptions)
    : WinDivertVpnAdapter(new WinDivertVpnAdapterSettings {
        AdapterName = "VpnHoodTestAdapter",
        Blocking = false,
        SimulateDns = vpnAdapterOptions.SimulateDns
    })
{
    public TestVpnAdapterOptions VpnAdapterOptions { get; } = vpnAdapterOptions;

    // the IP versions the host's network has, as a test plays a network; null: the machine's own
    public IReadOnlyList<IpVersion>? NetworkIpVersions { get; set; }

    public override bool IsIpVersionSupported(IpVersion ipVersion) =>
        NetworkIpVersions?.Contains(ipVersion) ?? base.IsIpVersionSupported(ipVersion);

    // as the adapter says once the host moved to other networks
    public void SimulatePrimaryNetworkChange() => OnPrimaryNetworkChanged();
}
