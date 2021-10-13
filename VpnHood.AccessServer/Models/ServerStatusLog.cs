using System;

namespace VpnHood.AccessServer.Models
{
    public class ServerStatusLog
    {
        public long ServerStatusLogId { get; set; }
        public Guid ServerId { get; set; }
        public int SessionCount { get; set; }
        public int TcpConnectionCount { get; set; }
        public int UdpConnectionCount { get; set; }
        public long FreeMemory { get; set; }
        public int ThreadCount { get; set; }
        public bool IsConfigure { get; set; }
        public DateTime CreatedTime { get; set; }
        public bool IsLast { get; set; }

        public virtual Server? Server { get; set; }
    }
}