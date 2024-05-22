using VpnHood.Common.Messaging;

namespace VpnHood.Tunneling.Messaging;

public class ServerStatusRequest() 
    : ClientRequest((byte)Messaging.RequestCode.ServerStatus)
{
    public string? Message { get; init; }
}