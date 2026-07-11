# VpnHood.Core.Quic.Abstractions

A minimal abstraction over the QUIC surface VpnHood actually uses, so the rest of the codebase does
**not** depend on `System.Net.Quic`/MsQuic directly.

VpnHood uses QUIC purely as a transport (a custom protocol with the same binary framing as TCP — **not**
HTTP/3; `SslApplicationProtocol.Http3` is used only as the ALPN token). `System.Net.Quic` is a managed
wrapper over the native **MsQuic** library and is only officially supported on **Windows and Linux** —
on Android/iOS `QuicConnection.IsSupported`/`QuicListener.IsSupported` are `false` and the APIs throw.
This abstraction lets us keep MsQuic as one implementation today and add platform-specific
implementations (e.g. P/Invoke to a native QUIC library) for iOS/Android later.

This project is **BCL-only** (no VpnHood or native dependencies) so it is safe to reference from the
low-level `VpnHood.Core.Toolkit`.

## Surface

| Type | Role |
|------|------|
| `IQuicClient` | Client connector — `ConnectAsync(QuicClientConnectOptions)` → `IQuicConnection` |
| `IQuicServer` | Server listener factory — `ListenAsync(QuicListenerOptions)` → `IQuicListener` |
| `IQuicListener` | Accepts inbound connections (server) — `AcceptConnectionAsync()` |
| `IQuicConnection` | Opens (`OpenOutboundStreamAsync`, client) / accepts (`AcceptInboundStreamAsync`, server) bidirectional streams as `System.IO.Stream` |
| `QuicClientConnectOptions` | `RemoteEndPoint`, `TargetHost`, `CertificateValidationCallback` |
| `QuicListenerOptions` | `ListenEndPoint`, `IdleTimeout`, `ServerCertificateSelector` |

Everything else VpnHood needs is constant and is baked into the implementation, not exposed here:
TLS 1.3, the `Http3` ALPN token, `EncryptionPolicy.RequireEncryption`, `X509RevocationMode.NoCheck`,
default stream/close error codes `0`, and `QuicStreamType.Bidirectional`.

## How it is wired into VpnHood

Implementations:

- **`VpnHood.Core.Quic.MsQuic`** (Windows / Linux) — `MsQuicClient`, `MsQuicServer`,
  `MsQuicConnection`, `MsQuicListener`, `MsQuicSocketFactory`. Owns the
  `Microsoft.Native.Quic.MsQuic.OpenSSL` native package. `MsQuicClient.IsSupported` /
  `MsQuicServer.IsSupported` report platform availability. Client **and** server.
- **`VpnHood.Core.Quic.Ios`** (iOS 15+) — `IosQuicClient`, `IosQuicConnection`, `IosQuicStream`,
  `IosQuicSocketFactory`, built on Apple's Network.framework (`NWMultiplexGroup` /
  `NWConnectionGroup` / `NWProtocolQuicOptions`). **Client only** (iOS is never a server).

### Client — through `ISocketFactory`
`ISocketFactory` (in `VpnHood.Core.Toolkit`) exposes QUIC capability:

```csharp
bool IsQuicSupported { get; }
IQuicClient CreateQuicClient(); // throws NotSupportedException when !IsQuicSupported
```

The per-platform `VpnService` picks the socket factory, which is the seam that decides QUIC support:

- Windows / Linux `VpnService` → `new MsQuicSocketFactory()` (QUIC enabled).
- iOS `VpnService` → `new IosQuicSocketFactory()` (QUIC enabled, iOS 15+).
- Android `VpnService` → plain `new SystemSocketFactory()` (`IsQuicSupported == false`) until a
  native implementation exists.

The socket factory flows `VpnService → VpnHoodClient → ConnectorService`, where
`QuicStreamConnectionFactory` is built from `socketFactory.CreateQuicClient()` (only when
`IsQuicSupported`). The decorators (`ConfiguringSocketFactory`, `BindingSocketFactory`,
`AdapterSocketFactory`) forward `IsQuicSupported`/`CreateQuicClient` to their inner factory.

### Server — explicit on `VpnHoodServer`
The server QUIC listener provider is passed explicitly (not via `ISocketFactory`):
`ServerOptions.QuicServer` → `VpnHoodServer` → `ServerHost` → `QuicListenerHost`. `ServerApp` sets it
to `MsQuicServer.IsSupported ? new MsQuicServer() : null`.

## Adding the Android implementation later

iOS is done via Network.framework (see `VpnHood.Core.Quic.Ios`). Android is the remaining gap —
`System.Net.Quic`/MsQuic is unsupported there.

1. Implement `IQuicClient` + `IQuicConnection` backed by a native QUIC library — e.g. P/Invoke to a
   self-built `libmsquic.so`, or a JNI binding to a Java/Kotlin QUIC library.
2. Expose it through an `ISocketFactory` whose `IsQuicSupported` is `true` and whose `CreateQuicClient()`
   returns your `IQuicClient`.
3. Have that platform's `VpnService` construct/use that socket factory instead of `SystemSocketFactory`.

No other VpnHood code needs to change — the abstraction is the single seam.
