using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Test;

public static class TestConstants
{
    public static TimeSpan DefaultHttpTimeout => TimeSpan.FromSeconds(3).WhenNoDebugger();
    public static TimeSpan DefaultUdpTimeout => DefaultHttpTimeout;
    public static TimeSpan DefaultPingTimeout => DefaultUdpTimeout;
    public static TimeSpan? DefaultQuicTimeout => DefaultUdpTimeout;
}