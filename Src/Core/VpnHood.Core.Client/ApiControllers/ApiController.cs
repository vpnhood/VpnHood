using VpnHood.Core.Client.Abstractions;

namespace VpnHood.Core.Client.ApiControllers;

// todo: temporary public. it should be internal
public class ApiController(VpnHoodClient client)
{
    public ConnectionInfo GetConnectionInfo()=> client.ConnectionInfo.ToDto();
    
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