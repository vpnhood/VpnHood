using System;
using System.Net;

namespace VpnHood.Messages
{
    public class TcpProxyChannelRequest
    {
        public ulong SessionId { get; set; }
        public string DestinationAddress { get; set; }
        public ushort DestinationPort { get; set; }

        public byte[] CipherSault { get; set; }
        public long CipherLength { get; set; }
    }
}
