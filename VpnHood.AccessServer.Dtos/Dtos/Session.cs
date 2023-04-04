using System;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Dtos;

public class Session
{
    public long SessionId { get; set; }
    public Guid AccessId { get; set; }
    public Guid DeviceId { get; set; }
    public string ClientVersion { get; set; } = default!;
    public string? DeviceIp { get; set; }
    public string? Country { get; set; }
    public byte[] SessionKey { get; set; } = default!;
    public Guid ServerId { get; set; }
    public DateTime CreatedTime { get; set; }
    public DateTime LastUsedTime { get; set; } 
    public DateTime? EndTime { get; set; }
    public SessionSuppressType SuppressedBy { get; set; }
    public SessionSuppressType SuppressedTo { get; set; }
    public SessionErrorCode ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}