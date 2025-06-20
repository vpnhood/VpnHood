using System.Text.Json.Serialization;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Server.Access.Messaging;

public class SessionUsage
{
    public required ulong SessionId { get; set; }

    [Obsolete("Use ErrorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Closed { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SessionErrorCode ErrorCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long Sent { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long Received { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? AdData { get; set; }
    public Traffic ToTraffic() => new(Sent, Received);
}