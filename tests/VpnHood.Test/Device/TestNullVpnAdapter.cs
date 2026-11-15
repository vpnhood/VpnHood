using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Test.Device;

public class TestNullVpnAdapter() : NullVpnAdapter(blocking: false)
{
    // whether the last start's options asked for exclusive DNS
    public bool IsDnsExclusive => UseExclusiveDns;
}