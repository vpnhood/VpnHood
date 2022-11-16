using System;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Dtos;

public class AccessPoint
{
    public Guid AccessPointId { get; set; }
    public string IpAddress { get; set; } = default!;
    public AccessPointMode AccessPointMode { get; set; }
    public bool IsListen { get; set; }
    public int TcpPort { get; set; }
    public int UdpPort { get; set; }
    public Guid AccessPointGroupId { get; set; }
    public string? AccessPointGroupName { get; set; }
    public Guid ServerId { get; set; }
}