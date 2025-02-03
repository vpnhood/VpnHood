using VpnHood.Core.Client.Abstractions;

namespace VpnHood.Core.Client.Controllers;

internal class ClientController
{
    void Start(ClientOptions clientOptions)
    {
        throw new NotImplementedException();
    }

    void Stop()
    {
        throw new NotImplementedException();
    }

    void Reconfigure()
    {
        throw new NotImplementedException();

    }

    public IConnectionInfo GetConnectionInfo()
    {
        // connect to the server and get the connection info
        throw new NotImplementedException();
    }
}


internal class CoreListener()
{
    //private TcpListener _tcpListener = new TcpListener();
    //private async Task Start()
    //{
    //    _tcpListener.Start();
    //    var tcpClient = await  _tcpListener.AcceptTcpClientAsync();
    //    ProcessClient(tcpClient);
    //}

    //private Task ProcessClient(TcpClient tcpClient)
    //{
    //    var tcpClient.GetStream().ReadAsync();
    //}
}

