using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Tunneling.Factory;

namespace VpnHood.Core.Client.Abstractions;

public class ClientController
{
    private readonly VpnHoodClient _client;
    private ClientController(VpnHoodClient client)
    {
        _client = client;
    }

    public static ClientController Create(IPacketCapture packetCapture, ISocketFactory socketFactory, string clientId, 
        Token token, ClientOptions clientOptions)
    {
        throw new NotImplementedException();
        //var client = new VpnHoodClient(packetCapture, socketFactory, clientId, token, clientOptions);
        //return new ClientController(client);
    }

    public Task Start(ClientOptions clientOptions, CancellationToken cancellationToken)
    {
        return _client.Connect(cancellationToken);
    }

    public Task Stop()
    {
        return _client.DisposeAsync().AsTask();
    }

    public Task Reconfigure()
    {
        throw new NotImplementedException();

    }

    public ConnectionInfo GetConnectionInfo()
    {
        // connect to the server and get the connection info
        throw new NotImplementedException();
    }
}