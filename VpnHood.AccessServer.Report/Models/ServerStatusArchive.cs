namespace VpnHood.AccessServer.Report.Models;

public class ServerStatusArchive
{
    public required long ServerStatusId { get; set; }
    public required Guid ProjectId { get; init; }
    public required Guid ServerId { get; init; }
    public required Guid ServerFarmId { get; init; }
    public required int SessionCount { get; set; }
    public required int TcpConnectionCount { get; set; }
    public required int UdpConnectionCount { get; set; }
    public required long? AvailableMemory { get; set; }
    public required byte? CpuUsage { get; set; }
    public required int ThreadCount { get; set; }
    public required long TunnelSendSpeed { get; set; }
    public required long TunnelReceiveSpeed { get; set; }
    public required bool IsConfigure { get; set; }
    public required DateTime CreatedTime { get; set; }
}