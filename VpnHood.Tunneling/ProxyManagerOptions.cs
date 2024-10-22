using VpnHood.Common.Logging;

namespace VpnHood.Tunneling;

public class ProxyManagerOptions
{
    public TimeSpan? UdpTimeout { get; set; }
    public TimeSpan? IcmpTimeout { get; set; }
    public int? MaxUdpClientCount { get; set; }
    public int? MaxIcmpClientCount { get; set; }
    public bool UseUdpProxy2 { get; set; }
    public int? UdpSendBufferSize { get; set; }
    public int? UdpReceiveBufferSize { get; set; }
    public LogScope? LogScope { get; set; }
}