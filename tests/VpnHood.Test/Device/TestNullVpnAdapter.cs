using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Test.Device;

public class TestNullVpnAdapter() : NullVpnAdapter(blocking: false);