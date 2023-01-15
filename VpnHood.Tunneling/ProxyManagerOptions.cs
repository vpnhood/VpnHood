using System;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Tunneling;

public class ProxyManagerOptions
{
    public TimeSpan? UdpTimeout { get; set; }
    public TimeSpan? IcmpTimeout { get; set; }
    public int? MaxUdpWorkerCount { get; set; }
    public int? MaxIcmpWorkerCount { get; set; }
}