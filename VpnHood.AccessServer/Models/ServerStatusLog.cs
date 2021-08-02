using System;

#nullable disable

namespace VpnHood.AccessServer.Models
{
    public partial class ServerStatusLog
    {
        public long ServerStatusLogId { get; set; }
        public Guid ServerId { get; set; }
        public int SessionCount { get; set; }
        public int NatTcpCount { get; set; }
        public int NatUdpCount { get; set; }
        public int FreeMemory { get; set; }
        public int ThreadCount { get; set; }
        public bool IsSubscribe { get; set; }
        public DateTime CreatedTime { get; set; }
        public bool IsLast { get; set; }
        
        public virtual Server Server { get; set; }
    }
}
