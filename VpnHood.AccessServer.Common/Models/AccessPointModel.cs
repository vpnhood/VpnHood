using VpnHood.AccessServer.Dtos;

namespace VpnHood.AccessServer.Models;

public class AccessPointModel
{
    public Guid AccessPointId { get; set; }
    public string IpAddress { get; set; } = default!;
    public AccessPointMode AccessPointMode { get; set; }
    public bool IsListen { get; set; }
    public int TcpPort { get; set; }
    public int UdpPort { get; set; }
    public Guid AccessPointGroupId { get; set; }
    public Guid ServerId { get; set; }
    public virtual ServerModel? Server { get; set; }
    public virtual AccessPointGroupModel? AccessPointGroup { get; set; }
}