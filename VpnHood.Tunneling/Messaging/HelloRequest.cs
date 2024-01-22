using VpnHood.Common.Messaging;

namespace VpnHood.Tunneling.Messaging;

public class HelloRequest(string requestId, string tokenId, ClientInfo clientInfo, byte[] encryptedClientId)
    : SessionRequest((byte)Messaging.RequestCode.Hello, requestId, tokenId, clientInfo, encryptedClientId);
