using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Controllers.DTOs
{
    public class ServerData
    {
        public Models.Server Server { get; set; } = null!;
        public ServerStatusLog? Status { get; set; }
    }
}
