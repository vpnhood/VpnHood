using System;
using System.IO;
using System.Threading.Tasks;
using VpnHood.Common.Net;

namespace VpnHood.Tunneling.ClientStreams;

public interface IClientStream : IAsyncDisposable
{
    IPEndPointPair IpEndPointPair { get; }
    Stream Stream { get; }
    public bool CheckIsAlive();
    public ValueTask DisposeAsync(bool allowReuse);
}