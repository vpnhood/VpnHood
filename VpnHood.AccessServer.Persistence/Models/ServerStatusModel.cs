namespace VpnHood.AccessServer.Persistence.Models;

public class ServerStatusBaseModel
{
    public required long ServerStatusId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid ServerFarmId { get; init; }
    public required Guid ServerId { get; init; }
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

public class ServerStatusModel : ServerStatusBaseModel
{
    public required bool IsLast { get; set; }
    public virtual ProjectModel? Project { get; set; }
    public virtual ServerModel? Server { get; set; }

}
