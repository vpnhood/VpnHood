namespace VpnHood.Core.Common.Messaging;

public class ClientInfo
{
    public required string ClientId { get; init; }
    public required string ClientVersion { get; init; }
    public int MinProtocolVersion { get; init; }
    public int MaxProtocolVersion { get; init; }
    public required string UserAgent { get; init; }

    public override string ToString()
    {
        return $"{nameof(ClientId)}={ClientId}";
    }
}