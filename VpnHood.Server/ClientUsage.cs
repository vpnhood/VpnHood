using System.Text.Json.Serialization;

namespace VpnHood.Server
{

    public class ClientUsage
    {
        public long SentByteCount { get; set; }
        public long ReceivedByteCount { get; set; }
    }
}