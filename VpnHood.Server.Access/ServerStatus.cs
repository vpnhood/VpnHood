namespace VpnHood.Server
{
    public class ServerStatus
    {
        public int SessionCount { get; set; }
        public int TcpConnectionCount { get; set; }
        public int UdpConnectionCount { get; set; }
        public long FreeMemory { get; set; }
        public long UsedMemory { get; set; }
        public int ThreadCount { get; set; }
        public long TunnelSendSpeed { get; set; }
        public long TunnelReceiveSpeed { get; set; }
    }
}