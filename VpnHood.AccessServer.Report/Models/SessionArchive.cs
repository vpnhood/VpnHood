namespace VpnHood.AccessServer.Report.Models;

public class SessionArchive
{
    public required long SessionId { get; set; }
    public required Guid ProjectId { get; init; }
    public required Guid AccessId { get; init; }
    public required Guid DeviceId { get; init; }
    public required Guid ServerId { get; init; }
    public required string ClientVersion { get; set; }
    public required string? DeviceIp { get; set; }
    public required string? Country { get; set; }
    public required DateTime CreatedTime { get; set; }
    public required DateTime LastUsedTime { get; set; }
    public required DateTime? EndTime { get; set; }
    public required int SuppressedBy { get; set; }
    public required int SuppressedTo { get; set; }
    public required int ErrorCode { get; set; }
    public required string? ErrorMessage { get; set; }
}