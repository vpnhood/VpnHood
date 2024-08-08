using System.Net;

namespace VpnHood.AccessServer.Dtos.HostOrders;

public class HostIp
{
    public required IPAddress IpAddress { get; init; }
    public required string HostProviderName { get; init; }
    public required DateTime CreatedTime { get; init; }
    public DateTime? ReleasedTime { get; set; }
    public Guid? ServerId { get; set; }
    public bool IsExtra { get; set; }
    public string? ProviderDescription { get; set; }
}