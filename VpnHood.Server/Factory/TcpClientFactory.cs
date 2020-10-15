using System.Net;
using System.Net.Sockets;

namespace VpnHood.Server.Factory
{
    public class TcpClientFactory
    {
        public virtual TcpClient CreateAndConnect(IPEndPoint remoteEP)
        {
            var tcpClient = new TcpClient
            {
                NoDelay = true
            };
            tcpClient.Connect(remoteEP);
            return tcpClient;
        }
    }
}


