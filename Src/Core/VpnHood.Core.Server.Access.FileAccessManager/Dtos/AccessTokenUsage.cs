using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Server.Access.Managers.FileAccessManagement.Dtos;

public class AccessTokenUsage
{
    public int Version { get; set; }
    public DateTime CreatedTime { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedTime { get; set; } = DateTime.UtcNow;
    public long Sent { get; set; }
    public long Received { get; set; }

    public Traffic ToTraffic() => new() { Sent = Sent, Received = Received };
}