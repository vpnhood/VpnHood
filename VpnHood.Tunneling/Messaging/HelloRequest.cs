using System;
using VpnHood.Common.Messaging;

namespace VpnHood.Tunneling.Messaging;

public class HelloRequest : SessionRequest
{
    public HelloRequest(string requestId, Guid tokenId, ClientInfo clientInfo, byte[] encryptedClientId)
        : base((byte)Messaging.RequestCode.Hello, requestId,  tokenId, clientInfo, encryptedClientId)
    {
    }

    //todo deprecated >= 416
    [Obsolete("Not needed for server >= 416")]
    public bool UseUdpChannel2 { get; set; } = true; 
}
