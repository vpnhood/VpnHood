using System.Collections.Generic;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Dtos;

public class ServerData
{
    public Server Server { get; set; } = null!;
    public ICollection<AccessPointModel>? AccessPoints { get; set; }
}