# VpnHood.Core.Quic.Android

Android implementation of the VpnHood QUIC abstractions (`IQuicClient` / `IQuicConnection` /
`Stream`), backed by **MsQuic** through **our own P/Invoke bindings** over a bundled `libmsquic.so`.

Unlike Windows/Linux (`VpnHood.Core.Quic.MsQuic`), this project does **not** use `System.Net.Quic`.
It is conceptually the Android sibling of `VpnHood.Core.Quic.Ios` (which is its own client over
Apple Network.framework): each mobile platform needs a native QUIC client because the BCL's QUIC
stack does not work there.

---

## Why not `System.Net.Quic` on Android?

We tried two simpler approaches first; both are dead ends. The history is worth keeping so nobody
repeats them:

1. **Reuse `MsQuicClient` (System.Net.Quic) + bundle `libmsquic.so`.**
   `QuicConnection.IsSupported` does become `true` once `libmsquic.so` is present, and the QUIC
   handshake even starts — but the connection then **hard-crashes (SIGSEGV)** inside
   `System.Net.Quic`'s certificate validation:
   ```
   #00 libcrypto.so  __aarch64_ldadd4_relax
   #01 libcrypto.so  X509_up_ref
   #02 libSystem.Security.Cryptography.Native.OpenSsl.so  CryptoNative_X509UpRef
   ```
   Root cause: `System.Net.Quic` validates the peer cert with the **OpenSSL** crypto backend
   (`X509_up_ref`, `CryptoNative_*`), but on Android `X509Certificate2`/`X509Chain` use the
   **Android** crypto backend (`libSystem.Security.Cryptography.Native.Android`). The cert handle
   it hands to OpenSSL is an Android handle, not an `X509*` — so it dereferences garbage. This is a
   fundamental backend split, **not fixable by shipping OpenSSL libs** (the cert object simply isn't
   an OpenSSL object). This is exactly why Microsoft lists `System.Net.Quic` as unsupported on Android.

2. **Bundle the OpenSSL crypto shim + `libcrypto`/`libssl` for Android.**
   Removes the `DllNotFoundException` but not the crash above — same backend split. Abandoned.

**The working approach (this project):** drive `libmsquic` directly via P/Invoke and do certificate
validation **ourselves** (msquic performs the TLS handshake with its own statically-linked OpenSSL;
we never touch OpenSSL from managed code).

---

## Architecture

| Type | Role |
|------|------|
| `Interop/MsQuicApi` | Opens the native `QUIC_API_TABLE` + one registration once per process (lazy, on the bundled `libmsquic.so`). `IsSupported` probes this. |
| `AndroidQuicClient : IQuicClient` | Opens a configuration (ALPN `h3`, client credential) + connection, starts it, awaits handshake, returns a connection. |
| `AndroidQuicConnection : IQuicConnection` | Owns the connection handle; routes the unmanaged connection callback; opens outbound / accepts inbound streams; performs **certificate validation**. |
| `AndroidQuicStream : Stream` | An async `Stream` over one msquic stream: RECEIVE → a `Pipe` that `ReadAsync` drains; `WriteAsync` → `StreamSend` awaited until `SEND_COMPLETE` (backpressure). |

This project is **pure managed code**. The native `libmsquic.so` and the MsQuic C# P/Invoke bindings
(`Microsoft.Quic` namespace) both come from the self-contained package
**`VpnHood.Core.Quic.MsQuic.AndroidNative`**, which this project references (a local `ProjectReference`
today, the published NuGet later). That package exposes the `Microsoft.Quic` binding types as **public**
API, so all native-linking complexity is hidden behind one reference. Unmanaged callbacks here are
`[UnmanagedCallersOnly(CallConvs=[CallConvCdecl])]` statics that resolve the managed instance from a
`GCHandle` passed as the callback context.

### Certificate validation (the important part)

The client credential uses:
```
CLIENT | INDICATE_CERTIFICATE_RECEIVED | DEFER_CERTIFICATE_VALIDATION | USE_PORTABLE_CERTIFICATES
```
- `INDICATE_CERTIFICATE_RECEIVED` — raises `PEER_CERTIFICATE_RECEIVED` so we can inspect the cert.
- `DEFER_CERTIFICATE_VALIDATION` — **required.** Without it msquic runs its own validation and rejects
  the (untrusted-on-Android) cert with TLS alert `UNKNOWN_CA` before our callback runs. With it, our
  callback's return value decides.
- `USE_PORTABLE_CERTIFICATES` — the peer cert + chain arrive as **DER** (`QUIC_BUFFER`s), not native
  handles.

In `PEER_CERTIFICATE_RECEIVED` we then replicate exactly what `SslStream`/System.Net.Quic would do,
using the **Android** crypto backend (no OpenSSL):
1. Build `X509Certificate2` from the leaf DER.
2. Add the intermediates from the event's **`Chain`** buffer to `X509Chain.ChainPolicy.ExtraStore`
   (otherwise the chain never builds → false `RemoteCertificateChainErrors`).
3. Compute `SslPolicyErrors`: `chain.Build(cert)` → `RemoteCertificateChainErrors`;
   `cert.MatchesHostname(TargetHost)` → `RemoteCertificateNameMismatch`.
4. Call VpnHood's `RemoteCertificateValidationCallback` (accepts when `errors == None`, or by pinned
   hash otherwise). Return `QUIC_STATUS_SUCCESS` / `QUIC_STATUS_BAD_CERTIFICATE`.

### SNI / connect target

`MsQuic.ConnectionStart`'s `ServerName` is used as **both** the connect target and the TLS SNI. We
must send the **`TargetHost`** as SNI (so the server returns the cert for that name) while connecting
to the exact resolved IP. So we set `QUIC_PARAM_CONN_REMOTE_ADDRESS` to `RemoteEndPoint` and pass
`TargetHost` as `ServerName`. (Sending the IP as SNI made the server return its *default* cert, whose
name did not match `TargetHost` → validation failed.)

---

## Native dependency & build

This project bundles **no** native libraries itself. Everything native comes from the referenced
**`VpnHood.Core.Quic.MsQuic.AndroidNative`** package, which ships the prebuilt **`libmsquic.so`** per ABI
(committed in that package's `native/` folder) plus the `Microsoft.Quic` bindings. msquic statically
links its own OpenSSL, so no `libcrypto`/`libssl`/.NET-OpenSSL-shim is needed.

- That package lives in the sibling repo **`VpnHood.Core.Quic.MsQuic.AndroidNative`** (a msquic+OpenSSL
  fork) at `android/AndroidNative/`. See its `android/DEV-GUIDE.md` for how the
  `.so` is produced (`build-android.ps1`); the package commits the binary so consumers never build it.
- The `<AndroidNativeLibrary>` items in that package flow **transitively** into any consuming APK
  (`lib/<abi>/libmsquic.so`), through this project and on to the app.
- Only **arm64-v8a** and **x86_64** are produced. 32-bit devices have no `libmsquic.so`, so
  `IsQuicSupported` is `false` there and the client transparently falls back to TCP.

Wiring: `AndroidQuicSocketFactory` (returned by `AndroidVpnService`) exposes `IsQuicSupported` /
`CreateQuicClient()` → `AndroidQuicClient`. QUIC engages only when the client's `ChannelProtocol`
is `Quic` and the server advertises a `QuicPort` (otherwise TCP).

---

## Testing (without the VPN)

A standalone test harness lives in `VpnHood.Core.Quic.MsQuic.AndroidNative/android/`:
- `quic-test-android/` — minimal APK that runs `AndroidQuicClient` against a
  [VpnHood.NetTester](https://github.com/vpnhood/VpnHood.NetTester) QUIC **echo** server.
- `quic-test/` — desktop runner of the same logic (sanity check on Windows).

Run the echo server: `nettester /ep <ip>:4040 /server`. The client POSTs a `ServerConfig`
(`QuicPort`, `Domain`, …) to `http://<ip>:4040/config`; the per-stream protocol is
`upSize(Int64) + downSize(Int64) + up bytes`, then read `down` bytes.

To `adb install` a Debug APK directly, build it self-contained
(`-p:EmbedAssembliesIntoApk=true`) — otherwise Fast Deployment leaves the assemblies off-device and
the app aborts at startup (`No assemblies found … Fast Deployment`).
