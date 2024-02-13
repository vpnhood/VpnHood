namespace VpnHood.Common.Messaging;

public abstract class SessionRequest(byte requestCode) : ClientRequest(requestCode)
{
    public required string TokenId { get; init; }
    public required ClientInfo ClientInfo { get; init; }
    public required byte[] EncryptedClientId { get; init; }
}