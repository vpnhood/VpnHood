using System;
using System.Net;

namespace VpnHood.Messages
{
    public class TcpProxyChannelRequest
    {
        public ulong SessionId { get; set; }
        public string DestinationAddress { get; set; }
        public ushort DestinationPort { get; set; }

        // -1 to use TLS for all stream
        public long TlsLength { get; set; } = -1; 
        public byte[] CipherIV { get; set; }
        public long CipherLength { get; set; }
    }
}
