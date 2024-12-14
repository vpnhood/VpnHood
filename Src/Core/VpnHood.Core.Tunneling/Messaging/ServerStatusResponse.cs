using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Tunneling.Messaging;

public class ServerStatusResponse : SessionResponse
{
    public string? Message { get; init; }
}