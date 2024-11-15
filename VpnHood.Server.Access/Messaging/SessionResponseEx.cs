using VpnHood.Common.Messaging;

namespace VpnHood.Server.Access.Messaging;

public class SessionResponseEx : SessionResponse
{
    public int ProtocolVersion { get; set; }
    public string? ExtraData { get; set; }
    public string? GaMeasurementId { get; set; }
    public string? AccessKey { get; set; }
    public DateTime? CreatedTime { get; set; }
    public SessionSuppressType SuppressedTo { get; set; }
    public ulong SessionId { get; set; }
    public byte[] SessionKey { get; set; } = [];
    public AdRequirement AdRequirement { get; set; } = AdRequirement.None;
    public string? ServerLocation { get; set; }
    public string[] ServerTags { get; set; } = [];
}