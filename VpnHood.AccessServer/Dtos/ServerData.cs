using System.Collections.Generic;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.ServerUtils;

namespace VpnHood.AccessServer.Dtos;

public class ServerData
{
    public Models.Server Server { get; set; } = null!;
    public ICollection<AccessPoint>? AccessPoints { get; set; }
    public ServerStatusEx? Status { get; set; }
    public ServerState State { get; set; }
}