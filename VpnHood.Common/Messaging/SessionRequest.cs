using System.Text.Json.Serialization;

namespace VpnHood.Common.Messaging;

public abstract class SessionRequest : ClientRequest
{
    [JsonConstructor]
    protected SessionRequest(byte requestCode, string requestId, string tokenId, ClientInfo clientInfo, byte[] encryptedClientId)
        : base(requestCode, requestId)
    {
        TokenId = tokenId;
        ClientInfo = clientInfo ?? throw new ArgumentNullException(nameof(clientInfo));
        EncryptedClientId = encryptedClientId ?? throw new ArgumentNullException(nameof(encryptedClientId));
    }

    protected SessionRequest(SessionRequest obj)
    : base(obj)
    {
        TokenId = obj.TokenId;
        ClientInfo = obj.ClientInfo;
        EncryptedClientId = obj.EncryptedClientId;
    }

    public string TokenId { get; init; }
    public ClientInfo ClientInfo { get; init; }
    public byte[] EncryptedClientId { get; init; }
}