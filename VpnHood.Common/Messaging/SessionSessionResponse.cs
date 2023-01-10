using System;
using System.Text.Json.Serialization;

namespace VpnHood.Common.Messaging;

public class SessionSessionResponse : SessionResponseBase
{
    [JsonConstructor]
    public SessionSessionResponse(SessionErrorCode errorCode)
        : base(errorCode)
    {
    }

    public SessionSessionResponse(SessionSessionResponse obj)
        : base(obj)
    {
        SessionId = obj.SessionId;
        SessionKey = obj.SessionKey;
        CreatedTime = obj.CreatedTime;
        SuppressedTo = obj.SuppressedTo;
    }

    public uint SessionId { get; set; }
    public byte[] SessionKey { get; set; } = Array.Empty<byte>();
    public DateTime? CreatedTime { get; set; }
    public SessionSuppressType SuppressedTo { get; set; }
}