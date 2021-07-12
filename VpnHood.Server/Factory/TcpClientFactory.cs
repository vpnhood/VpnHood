using System.Net;
using System.Net.Sockets;

namespace VpnHood.Server.Factory
{
    public class TcpClientFactory
    {
        public virtual TcpClient Create() => new ();
    }
}


