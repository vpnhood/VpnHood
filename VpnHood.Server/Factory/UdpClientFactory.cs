using System.Net.Sockets;

namespace VpnHood.Server.Factory
{
    public class UdpClientFactory
    {
        public virtual UdpClient CreateListner()
        {
            var udpClient = new UdpClient(0);
            return udpClient;
        }
    }
}


