using System.Text.Json.Serialization;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Models;

public class Session
{
    public Session()
    {

    }

    public Session(long sessionId)
    {
        SessionId = sessionId;
    }

    public long SessionId { get; set; }
    public Guid AccessId { get; set; }
    public Guid DeviceId { get; set; }
    public string ClientVersion { get; set; } = null!;
    public string? DeviceIp { get; set; }
    public string? Country { get; set; }
    public byte[] SessionKey { get; set; } = null!;
    public Guid ServerId { get; set; }
    public DateTime CreatedTime { get; set; } = DateTime.UtcNow;
    public DateTime AccessedTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public SessionSuppressType SuppressedBy { get; set; }
    public SessionSuppressType SuppressedTo { get; set; }
    public SessionErrorCode ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public virtual Server? Server { get; set; }
    public virtual Device? Device { get; set; }
    public virtual Access? Access { get; set; }
    public bool IsEndTimeSaved { get; set; } // should not be saved

    [JsonIgnore] public virtual ICollection<AccessUsageEx>? AccessUsages { get; set; }
}