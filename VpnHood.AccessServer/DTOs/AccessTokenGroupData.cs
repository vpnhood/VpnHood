using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Controllers.DTOs
{
    public class AccessTokenGroupData
    {
        public AccessTokenGroup AccessTokenGroup { get; set; } = null!;
        public ServerEndPoint? DefaultServerEndPoint { get; set; }
    }

}
