using System.Collections.Generic;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DTOs
{
    public class ServerData
    {
        public Models.Server Server { get; set; } = null!;
        public ICollection<AccessPoint>? AccessPoints { get; set; }
        public ServerStatusEx? Status { get; set; }
        public ServerState State { get; set; }
    }
}