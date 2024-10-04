using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Dtos;

public class Session
{
    public required ulong SessionId { get; init; }
    public required Guid AccessId { get; init; }
    public required Guid DeviceId { get; init; }
    public required string ClientVersion { get; init; } = default!;
    public required string? DeviceIp { get; init; }
    public required string? Country { get; init; }
    public required byte[] SessionKey { get; init; } = default!;
    public required string? ExtraData { get; init; }
    public Guid ServerId { get; init; }
    public DateTime CreatedTime { get; init; }
    public DateTime LastUsedTime { get; init; }
    public DateTime? EndTime { get; init; }
    public SessionSuppressType SuppressedBy { get; init; }
    public SessionSuppressType SuppressedTo { get; init; }
    public SessionErrorCode ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}