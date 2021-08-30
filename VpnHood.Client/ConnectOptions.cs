namespace VpnHood.Client
{
    public class ConnectOptions
    {
        public int MaxReconnectCount { get; set; } = 3;

        /// <summary>
        ///     Time in millisecond
        /// </summary>
        public int ReconnectDelay { get; set; } = 10 * 1000;

        public UdpChannelMode UdpChannelMode { get; set; } = UdpChannelMode.Off;
    }
}