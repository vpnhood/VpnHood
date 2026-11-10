# VpnHood.Net.Quic.MsQuic

The Windows and Linux implementation of `VpnHood.Net.Quic.Abstractions`, over .NET's `System.Net.Quic` and the
native MsQuic library: the QUIC client and server of [VpnHood](https://github.com/vpnhood/VpnHood)'s transport.

```bash
dotnet add package VpnHood.Net.Quic.MsQuic
```

## How it works

- `MsQuicClient` connects with TLS 1.3 and the ALPN token `h3`, and leaves the server's certificate to the
  connect options' `CertificateValidationCallback`. `MsQuicClient.IsSupported` tells whether the platform has
  QUIC.
- `MsQuicServer` listens; it takes its certificate from the listener options' `ServerCertificateSelector`. Its
  connections implement `IQuicServerConnection`, and their streams `IQuicStream`. `MsQuicServer.IsSupported`
  tells whether the platform can listen.
- `MsQuicClientFactory` is the platform's `IQuicClientFactory`, which makes `MsQuicClient`s.
- The package references `Microsoft.Native.Quic.MsQuic.OpenSSL`, which brings a native MsQuic library.

## Packages

- `VpnHood.Net.Quic.MsQuic` — this implementation
- `VpnHood.Net.Quic.Abstractions` — the contract; `VpnHood.Net.Quic.Android` and `VpnHood.Net.Quic.Ios` are the
  mobile clients

Requires .NET 11.
