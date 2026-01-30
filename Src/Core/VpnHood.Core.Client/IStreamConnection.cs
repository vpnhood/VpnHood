using System.Net;

namespace VpnHood.Core.Client;

public interface IStreamConnection
{
    Stream Stream { get; }
    IPEndPoint LocalEndPoint { get; }
    IPEndPoint RemoteEndPoint { get; }
}