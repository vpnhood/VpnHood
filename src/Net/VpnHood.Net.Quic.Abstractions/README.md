# VpnHood.Net.Quic.Abstractions

A minimal abstraction over the QUIC surface VpnHood actually uses, so the rest of the codebase does
**not** depend on `System.Net.Quic`/MsQuic directly.

VpnHood uses QUIC purely as a transport (a custom protocol with the same binary framing as TCP — **not**
HTTP/3; `SslApplicationProtocol.Http3` is used only as the ALPN token). `System.Net.Quic` is a managed
wrapper over the native **MsQuic** library and is only officially supported on **Windows and Linux** —
on Android/iOS `QuicConnection.IsSupported`/`QuicListener.IsSupported` are `false` and the APIs throw.
This abstraction keeps MsQuic as one implementation beside the platform-specific ones for iOS and
Android.

This project is **BCL-only** (no VpnHood or native dependencies). `VpnHood.Net.Toolkit` does not reference
it: Toolkit references no other project.

## Surface

| Type | Role |
|------|------|
| `IQuicClientFactory` | A platform's QUIC — `IsSupported`, `CreateClient()` → `IQuicClient` |
| `IQuicClient` | Client connector — `ConnectAsync(QuicClientConnectOptions, ct)` → `IQuicConnection` |
| `IQuicServer` | Server listener factory — `ListenAsync(QuicListenerOptions, ct)` → `IQuicListener` |
| `IQuicListener` | Accepts inbound connections (server) — `AcceptConnectionAsync(ct)` |
| `IQuicConnection` | Opens (`OpenOutboundStreamAsync(ct)`) and accepts (`AcceptInboundStreamAsync(ct)`) streams as `System.IO.Stream`; both sides do both |
| `IQuicServerConnection` | A server's connection: also opens write-only streams (`OpenOutboundUnidirectionalStreamAsync(ct)`) and closes with an error code (`CloseAsync(errorCode, ct)`) |
| `IQuicStream` | The QUIC controls of a server's stream: `StreamId`, `AbortRead`/`AbortWrite` codes, `CompleteWrites`, `WriteFinal`, `ReadsClosed`/`WritesClosed` |
| `QuicClientConnectOptions` | `RemoteEndPoint`, `TargetHost`, `CertificateValidationCallback`, `MaxInboundBidirectionalStreams` |
| `QuicListenerOptions` | `ListenEndPoint`, `IdleTimeout`, `ServerCertificateSelector`, stream limits and default error codes |

The implementations set what VpnHood keeps constant, which is not exposed here: TLS 1.3, the `Http3` ALPN
token and `EncryptionPolicy.RequireEncryption`; MsQuic also sets `X509RevocationMode.NoCheck`. Each client
leaves the server's certificate to `CertificateValidationCallback`; none pins a certificate itself.

## How it is wired into VpnHood

Implementations:

- **`VpnHood.Net.Quic.MsQuic`** (Windows / Linux) — `MsQuicClient`, `MsQuicServer`,
  `MsQuicConnection`, `MsQuicListener`, `MsQuicClientFactory`. Owns the
  `Microsoft.Native.Quic.MsQuic.OpenSSL` native package. `MsQuicClient.IsSupported` /
  `MsQuicServer.IsSupported` report platform availability. Client **and** server.
- **`VpnHood.Net.Quic.Ios`** (iOS 15+) — `IosQuicClient`, `IosQuicConnection`, `IosQuicStream`,
  `IosQuicClientFactory`, built on Apple's Network.framework (`NWMultiplexGroup` /
  `NWConnectionGroup` / `NWProtocolQuicOptions`). **Client only** (iOS is never a server).
- **`VpnHood.Net.Quic.Android`** — `AndroidQuicClient`, `AndroidQuicConnection`, `AndroidQuicStream`,
  `AndroidQuicClientFactory`, over a `libmsquic.so` built for Android (see that project's README).
  **Client only**.

### Client — the QUIC of the client's socket factory
`IQuicClientFactory` is the platform's QUIC:

```csharp
bool IsSupported { get; }
IQuicClient CreateClient(); // throws NotSupportedException when !IsSupported
```

The per-platform `VpnService` hands `VpnServiceHost` one `IClientSocketFactory` (VpnHood.Core.Client): a
`ClientSocketFactory` of a `SystemSocketFactory` for its sockets and the platform's QUIC client factory, which is the
seam that decides QUIC support:

- Windows / Linux `VpnService` → `new MsQuicClientFactory()`.
- iOS `VpnService` → `new IosQuicClientFactory(…)` (iOS 15+).
- Android `VpnService` → `new AndroidQuicClientFactory()` (wherever the native library loads).
- null: no QUIC; the client keeps to TCP.

It flows `VpnServiceHost → VpnHoodClient → ClientSessionBuilder → ConnectorService`, where
`QuicStreamConnectionFactory` is built from `CreateClient()` (only when `IsSupported`). The client wraps the
sockets (protection, binding, configuring) and keeps the QUIC client factory as it is: those wrappers know nothing
of QUIC.

### Server — explicit on `VpnHoodServer`
The server QUIC listener provider is passed explicitly (not via `ISocketFactory`):
`ServerOptions.QuicServer` → `VpnHoodServer` → `ServerHost` → `QuicListenerHost`. `ServerApp` sets it
to `MsQuicServer.IsSupported ? new MsQuicServer() : null`.

## Adding another platform

1. Implement `IQuicClient` + `IQuicConnection` over the platform's QUIC, as `VpnHood.Net.Quic.Ios`
   does over Network.framework and `VpnHood.Net.Quic.Android` over a self-built `libmsquic.so`.
2. Expose it through an `IQuicClientFactory` whose `IsSupported` is `true` and whose `CreateClient()`
   returns your `IQuicClient`.
3. Have that platform's `VpnService` hand `VpnServiceHost` a `ClientSocketFactory` with that factory as its
   `quicClientFactory`.

No other VpnHood code needs to change — the abstraction is the single seam.
