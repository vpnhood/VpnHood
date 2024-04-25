namespace VpnHood.Client;

public class ConnectOptions
{
    public int MaxReconnectCount { get; set; } = 3;
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(20);
    public UdpChannelMode UdpChannelMode { get; set; } = UdpChannelMode.Off;
}