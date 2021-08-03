namespace VpnHood.Server
{
    public class ServerStatus
    {
        public int SessionCount {get;set;}
        public int NatTcpCount {get;set;}
        public int NatUdpCount { get;set;}
        public int FreeMemory { get; set; }
        public int ThreadCount { get; set; }
    }
}
