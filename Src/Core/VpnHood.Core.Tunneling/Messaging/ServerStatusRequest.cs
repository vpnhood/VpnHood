using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Tunneling.Messaging;

// todo: deprecated
public class ServerStatusRequest()
    : ClientRequest((byte)Messaging.RequestCode.ServerStatus)
{
    public string? Message { get; init; }
}