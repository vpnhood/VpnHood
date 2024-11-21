﻿using VpnHood.Common.Net;

namespace VpnHood.Tunneling.ClientStreams;

public interface IClientStream : IAsyncDisposable
{
    bool Disposed { get; }
    string ClientStreamId { get; set; }
    bool RequireHttpResponse { get; set; }
    IPEndPointPair IpEndPointPair { get; }
    Stream Stream { get; }
    bool CheckIsAlive();
    ValueTask DisposeAsync(bool graceful = true);
}