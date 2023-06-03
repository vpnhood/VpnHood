using System;
using System.IO;
using VpnHood.Common.Net;

namespace VpnHood.Tunneling.ClientStreams;

public interface IClientStream : IDisposable
{
    IPEndPointPair IpEndPointPair { get; }
    Stream Stream { get; }
    bool NoDelay { get; set; }
    public int ReceiveBufferSize { get; set; }
    public int SendBufferSize { get; set; }
    public bool CheckIsAlive();
}