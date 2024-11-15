using VpnHood.Common.Messaging;

namespace VpnHood.Server.Access.Managers.FileAccessManagers.Dtos;

public class AccessTokenUsage
{
    [Obsolete("Use SentTraffic instead.")]
    public long SentTraffic {
        set => Sent = value;
    }

    [Obsolete("Use Received instead.")]
    public long ReceivedTraffic {
        set => Received = value;
    }

    public long Sent { get; set; }
    public long Received { get; set; }

    public Traffic ToTraffic() => new() { Sent = Sent, Received = Received };
}