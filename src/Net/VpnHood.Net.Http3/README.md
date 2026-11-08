# VpnHood.Net.Http3

A server-side HTTP/3 engine for .NET over the `VpnHood.Net.Quic.Abstractions` contract, built so another
protocol can share a QUIC port with HTTP/3: the first bytes of each request stream go to a filter, which claims
the stream for its own protocol or leaves it to HTTP/3. It is how a [VpnHood](https://github.com/vpnhood/VpnHood)
server answers on its QUIC port the way a plain HTTP/3 server does.

```bash
dotnet add package VpnHood.Net.Http3
```

## How it works

- `Http3ServerConnection` serves one QUIC connection. `Run` accepts its streams until the connection closes,
  stays idle for `KeepAliveTimeout`, or is canceled, then closes and disposes it; `Close` ends it with an
  `Http3ErrorCode`.
- `IHttp3StreamFilter` sees the start of each request stream and answers `Claimed`, `NotClaimed` or `NeedMore`,
  within `MaxFilterBytes`.
- `IHttp3StreamRunner` runs a claimed stream, which reads its first bytes again.
- `IHttp3Responder` answers every other request with a header-only response, through `Http3ResponseWriter`. The
  engine inspects up to `MaxBodyInspectionBytes` after the headers to check the body, and discards it.
- `Http3ServerOptions` sets the limits and timeouts; a request whose header fields are over the limits when the
  response is chosen gets status 431.
- QPACK runs without a dynamic table, so the engine opens no encoder or decoder streams.

## Packages

- `VpnHood.Net.Http3` — the engine
- `VpnHood.Net.Quic.Abstractions` — the QUIC contract it serves; `VpnHood.Net.Quic.MsQuic` implements its
  server side

Requires .NET 10.
