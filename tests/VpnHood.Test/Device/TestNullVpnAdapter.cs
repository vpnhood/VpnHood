using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Test.Device;

public class TestNullVpnAdapter() : NullVpnAdapter(autoDisposePackets: true, blocking: false);