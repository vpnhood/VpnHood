using System.Net.Sockets;

namespace VpnHood.Server.Factory
{
    public class UdpClientFactory
    {
        public virtual UdpClient Create() => new(0);
    }
}


