namespace VpnHood.Common.Messaging;

public class ClientInfo
{
    public required string ClientId { get; init; }
    public required string ClientVersion { get; init; }
    public required int ProtocolVersion { get; init; }
    public required string UserAgent { get; init; }

    public override string ToString()
    {
        return $"{nameof(ClientId)}={ClientId}";
    }
}