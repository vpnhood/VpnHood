﻿using System.Text.Json.Serialization;
using VpnHood.Common.Messaging;

namespace VpnHood.Server.Access.Messaging;

public class SessionResponseEx : SessionResponse
{
    public int ProtocolVersion { get; set; }
    public string? ExtraData { get; set; }
    public string? GaMeasurementId { get; set; }
    public DateTime? CreatedTime { get; set; }
    public ulong SessionId { get; set; }
    public byte[] SessionKey { get; set; } = [];
    public string? ServerLocation { get; set; }
    public string[] ServerTags { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AdRequirement AdRequirement { get; set; } = AdRequirement.None;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SessionSuppressType SuppressedTo { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsReviewRequested { get; set; }
}