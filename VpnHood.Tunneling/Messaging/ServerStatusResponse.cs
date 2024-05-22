using VpnHood.Common.Messaging;

namespace VpnHood.Tunneling.Messaging;

public class ServerStatusResponse : SessionResponse
{
    public string? Message { get; init; }
}