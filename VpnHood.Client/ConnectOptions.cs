namespace VpnHood.Client;

public class ConnectOptions
{
    public int MaxReconnectCount { get; set; } = 3;

    /// <summary>
    ///     Time in millisecond
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(10); //to do : should be 20 seconds

    public UdpChannelMode UdpChannelMode { get; set; } = UdpChannelMode.Off;
}