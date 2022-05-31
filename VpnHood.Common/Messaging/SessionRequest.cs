using System;
using System.Text.Json.Serialization;

namespace VpnHood.Common.Messaging;

public class SessionRequest
{
    [JsonConstructor]
    public SessionRequest(Guid tokenId, ClientInfo clientInfo, byte[] encryptedClientId)
    {
        TokenId = tokenId;
        ClientInfo = clientInfo ?? throw new ArgumentNullException(nameof(clientInfo));
        EncryptedClientId = encryptedClientId ?? throw new ArgumentNullException(nameof(encryptedClientId));
    }

    public SessionRequest(SessionRequest obj)
    {
        TokenId = obj.TokenId;
        ClientInfo = obj.ClientInfo;
        EncryptedClientId = obj.EncryptedClientId;
    }

    public Guid TokenId { get; set; }
    public ClientInfo ClientInfo { get; set; }
    public byte[] EncryptedClientId { get; set; }
}