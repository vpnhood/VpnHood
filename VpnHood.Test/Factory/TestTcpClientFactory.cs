using VpnHood.Server;
using VpnHood.Server.Factory;
using System;
using System.Net;
using System.Net.Sockets;

namespace VpnHood.Test.Factory
{
    public class TestTcpClientFactory : TcpClientFactory
    {
        public override TcpClient CreateAndConnect(IPEndPoint remoteEP)
        {
            for (var i=TestPacketCapture.ServerMinPort; i<= TestPacketCapture.ServerMaxPort; i++ )
            {
                try
                {
                    var localEndPoint = new IPEndPoint(IPAddress.Any, i);
                    var tcpClient = new TcpClient(localEndPoint) { NoDelay = true };
                    tcpClient.Connect(remoteEP);
                    return tcpClient;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    // try next
                }
            }

            throw new Exception("Could not find free port for test!");
        }
    }
}
