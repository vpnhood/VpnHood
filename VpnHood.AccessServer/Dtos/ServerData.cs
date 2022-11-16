using System.Collections.Generic;

namespace VpnHood.AccessServer.Dtos;

public class ServerData
{
    public Server Server { get; set; } = null!;
    public ICollection<Models.AccessPointModel>? AccessPoints { get; set; }
}