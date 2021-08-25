using System.Text.Json.Serialization;

namespace VpnHood.Tunneling.Messaging
{
    public class TcpDatagramChannelRequest : RequestBase
    {
        [JsonConstructor]
        public TcpDatagramChannelRequest(uint sessionId, byte[] sessionKey) 
            : base(sessionId, sessionKey)
        {
        }
    }
}
