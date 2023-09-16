using System;
using VpnHood.Common.Messaging;

namespace VpnHood.Tunneling.Messaging;

public class HelloRequest : SessionRequest
{
    public HelloRequest(string requestId, Guid tokenId, ClientInfo clientInfo, byte[] encryptedClientId)
        : base((byte)Messaging.RequestCode.Hello, requestId,  tokenId, clientInfo, encryptedClientId)
    {
    }
}
