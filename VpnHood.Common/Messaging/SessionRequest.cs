using System;
using System.Text.Json.Serialization;

namespace VpnHood.Common.Messaging;

public class SessionRequest
{
    [JsonConstructor]
    public SessionRequest(Guid tokenId, ClientInfo clientInfo, byte[] encryptedClientId, string? requestId = null)
    {
        TokenId = tokenId;
        ClientInfo = clientInfo ?? throw new ArgumentNullException(nameof(clientInfo));
        EncryptedClientId = encryptedClientId ?? throw new ArgumentNullException(nameof(encryptedClientId));
        RequestId = requestId ?? "OldVersion"; //must be required after >= 3.0.371
    }

    public SessionRequest(SessionRequest obj)
    {
        TokenId = obj.TokenId;
        ClientInfo = obj.ClientInfo;
        EncryptedClientId = obj.EncryptedClientId;
        RequestId = obj.RequestId;
    }

    public Guid TokenId { get; init; }
    public ClientInfo ClientInfo { get; init; }
    public byte[] EncryptedClientId { get; init; }
    public string RequestId { get; set; } 
}