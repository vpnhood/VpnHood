# VpnHood.Net.Quic.Abstractions

A minimal abstraction over the QUIC surface VpnHood actually uses, so the rest of the codebase does
**not** depend on `System.Net.Quic`/MsQuic directly.

VpnHood uses QUIC purely as a transport (a custom protocol with the same binary framing as TCP — **not**
HTTP/3; `SslApplicationProtocol.Http3` is used only as the ALPN token). `System.Net.Quic` is a managed
wrapper over the native **MsQuic** library and is only officially supported on **Windows and Linux** —
on Android/iOS `QuicConnection.IsSupported`/`QuicListener.IsSupported` are `false` and the APIs throw.
This abstraction keeps MsQuic as one implementation beside the platform-specific ones for iOS and
Android.

This project is **BCL-only** (no VpnHood or native dependencies) so it is safe to reference from the
low-level `VpnHood.Net.Toolkit`.

## Surface

| Type | Role |
|------|------|
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
  `MsQuicConnection`, `MsQuicListener`, `MsQuicSocketFactory`. Owns the
  `Microsoft.Native.Quic.MsQuic.OpenSSL` native package. `MsQuicClient.IsSupported` /
  `MsQuicServer.IsSupported` report platform availability. Client **and** server.
- **`VpnHood.Net.Quic.Ios`** (iOS 15+) — `IosQuicClient`, `IosQuicConnection`, `IosQuicStream`,
  `IosSocketFactory`, built on Apple's Network.framework (`NWMultiplexGroup` /
  `NWConnectionGroup` / `NWProtocolQuicOptions`). **Client only** (iOS is never a server).
- **`VpnHood.Net.Quic.Android`** — `AndroidQuicClient`, `AndroidQuicConnection`, `AndroidQuicStream`,
  `AndroidSocketFactory`, over a `libmsquic.so` built for Android (see that project's README).
  **Client only**.

### Client — through `ISocketFactory`
`ISocketFactory` (in `VpnHood.Net.Toolkit`) exposes QUIC capability:

```csharp
bool IsQuicSupported { get; }
IQuicClient CreateQuicClient(); // throws NotSupportedException when !IsQuicSupported
```

The per-platform `VpnService` picks the socket factory, which is the seam that decides QUIC support:

- Windows / Linux `VpnService` → `new MsQuicSocketFactory()` (QUIC enabled).
- iOS `VpnService` → `new IosSocketFactory(…)` (QUIC enabled, iOS 15+).
- Android `VpnService` → `new AndroidSocketFactory()` (QUIC wherever the native library loads).

The socket factory flows `VpnService → VpnHoodClient → ConnectorService`, where
`QuicStreamConnectionFactory` is built from `socketFactory.CreateQuicClient()` (only when
`IsQuicSupported`). The decorators (`ConfiguringSocketFactory`, `BindingSocketFactory`,
`AdapterSocketFactory`) forward `IsQuicSupported`/`CreateQuicClient` to their inner factory.

### Server — explicit on `VpnHoodServer`
The server QUIC listener provider is passed explicitly (not via `ISocketFactory`):
`ServerOptions.QuicServer` → `VpnHoodServer` → `ServerHost` → `QuicListenerHost`. `ServerApp` sets it
to `MsQuicServer.IsSupported ? new MsQuicServer() : null`.

## Adding another platform

1. Implement `IQuicClient` + `IQuicConnection` over the platform's QUIC, as `VpnHood.Net.Quic.Ios`
   does over Network.framework and `VpnHood.Net.Quic.Android` over a self-built `libmsquic.so`.
2. Expose it through an `ISocketFactory` whose `IsQuicSupported` is `true` and whose `CreateQuicClient()`
   returns your `IQuicClient`.
3. Have that platform's `VpnService` construct/use that socket factory instead of `SystemSocketFactory`.

No other VpnHood code needs to change — the abstraction is the single seam.
