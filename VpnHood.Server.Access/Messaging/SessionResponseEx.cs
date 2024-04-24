using System.Text.Json.Serialization;
using VpnHood.Common.Messaging;

namespace VpnHood.Server.Access.Messaging;

public class SessionResponseEx : SessionResponse
{
    [JsonIgnore(Condition =JsonIgnoreCondition.WhenWritingNull)]
    public string? ExtraData { get; set; }
    public string? GaMeasurementId { get; set; }
    public string? AccessKey { get; set; }
    public DateTime? CreatedTime { get; set; }
    public SessionSuppressType SuppressedTo { get; set; }
    public ulong SessionId { get; set; }
    public byte[] SessionKey { get; set; } = Array.Empty<byte>();
    public bool IsAdRequired { get; set; }
}