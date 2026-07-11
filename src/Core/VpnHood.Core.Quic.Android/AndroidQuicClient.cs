using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Quic;
using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Quic.Droid.Interop;
using VpnHood.Core.Toolkit.Utils;
using static Microsoft.Quic.MsQuic;

namespace VpnHood.Core.Quic.Droid;

/// <summary>
/// Client-side QUIC connector for Android, backed by MsQuic (libmsquic.so) through our own P/Invoke
/// bindings. Unlike the desktop/Linux path it does NOT use System.Net.Quic, whose managed TLS
/// certificate validation is incompatible with Android's crypto backend; instead msquic performs the
/// TLS handshake (its own statically-linked OpenSSL) and we validate the peer certificate ourselves
/// against VpnHood's pinned hash. Matches the desktop MsQuic client: ALPN "h3", TLS 1.3.
/// </summary>
public sealed class AndroidQuicClient : IQuicClient
{
    public static bool IsSupported => OperatingSystem.IsAndroid() && MsQuicApi.IsSupported;

    public async ValueTask<IQuicConnection> ConnectAsync(
        QuicClientConnectOptions options, CancellationToken cancellationToken)
    {
        if (!IsSupported)
            throw new NotSupportedException("QUIC is not supported on this device.");

        // All native-pointer work happens in synchronous helpers so no pointer locals cross 'await'.
        var (conn, config, gch, state) = StartConnect(options);

        var reg = cancellationToken.Register(
            static s => ((AndroidQuicConnectionState)s!).Connected.TrySetCanceled(), state);
        try {
            await state.Connected.Task.Vhc();
        }
        catch {
            CloseHandles(conn, config, gch);
            throw;
        }
        finally {
            await reg.DisposeAsync().Vhc();
        }

        return CreateConnection(conn, config, gch, state, options.RemoteEndPoint);
    }

    private static unsafe (IntPtr conn, IntPtr config, GCHandle gch, AndroidQuicConnectionState state)
        StartConnect(QuicClientConnectOptions options)
    {
        var table = MsQuicApi.Table;
        var registration = MsQuicApi.Registration;
        QUIC_HANDLE* config = null;
        QUIC_HANDLE* conn = null;
        var gch = default(GCHandle);
        // ServerName is the TLS SNI: it must be the target host so the server returns the matching cert
        // (NOT the IP). The actual connect target is pinned to RemoteEndPoint via QUIC_PARAM_CONN_REMOTE_ADDRESS
        // below, so msquic connects to the exact IP and uses ServerName only for SNI. msquic copies the
        // ServerName during ConnectionStart, so it is freed in the finally.
        var sni = string.IsNullOrEmpty(options.TargetHost)
            ? options.RemoteEndPoint.Address.ToString()
            : options.TargetHost;
        var serverName = Marshal.StringToCoTaskMemUTF8(sni);
        try {
            var alpn = stackalloc byte[2] { (byte)'h', (byte)'3' };
            var alpnBuf = new QUIC_BUFFER { Length = 2, Buffer = alpn };
            ThrowIfFailure(table->ConfigurationOpen(registration, &alpnBuf, 1, null, 0, null, &config));

            // INDICATE_CERTIFICATE_RECEIVED: raise PEER_CERTIFICATE_RECEIVED so we can validate.
            // DEFER_CERTIFICATE_VALIDATION: do NOT let msquic reject on its own (Android has no CA store,
            //   so it would fail with UNKNOWN_CA) — our callback's return value decides instead.
            // USE_PORTABLE_CERTIFICATES: deliver the peer cert as portable DER bytes.
            var cred = new QUIC_CREDENTIAL_CONFIG {
                Type = QUIC_CREDENTIAL_TYPE.NONE,
                Flags = QUIC_CREDENTIAL_FLAGS.CLIENT
                        | QUIC_CREDENTIAL_FLAGS.INDICATE_CERTIFICATE_RECEIVED
                        | QUIC_CREDENTIAL_FLAGS.DEFER_CERTIFICATE_VALIDATION
                        | QUIC_CREDENTIAL_FLAGS.USE_PORTABLE_CERTIFICATES
            };
            ThrowIfFailure(table->ConfigurationLoadCredential(config, &cred));

            var state = new AndroidQuicConnectionState {
                CertificateValidationCallback = options.CertificateValidationCallback,
                TargetHost = options.TargetHost
            };
            gch = GCHandle.Alloc(state);

            ThrowIfFailure(table->ConnectionOpen(registration,
                &AndroidQuicConnection.ConnectionCallback, (void*)GCHandle.ToIntPtr(gch), &conn));

            // Pin the connect target to the exact resolved endpoint so msquic does NOT re-resolve the SNI host.
            var addr = ToQuicAddr(options.RemoteEndPoint);
            ThrowIfFailure(table->SetParam(conn, QUIC_PARAM_CONN_REMOTE_ADDRESS, (uint)sizeof(QuicAddr), &addr));

            ThrowIfFailure(table->ConnectionStart(conn, config, (ushort)QUIC_ADDRESS_FAMILY_UNSPEC,
                (sbyte*)serverName, (ushort)options.RemoteEndPoint.Port));

            return ((IntPtr)conn, (IntPtr)config, gch, state);
        }
        catch {
            if (conn != null) table->ConnectionClose(conn);
            if (config != null) table->ConfigurationClose(config);
            if (gch.IsAllocated) gch.Free();
            throw;
        }
        finally {
            Marshal.FreeCoTaskMem(serverName);
        }
    }

    // Builds an MsQuic QUIC_ADDR (socketAddr) from an IPEndPoint (port stored in network byte order).
    private static unsafe QuicAddr ToQuicAddr(IPEndPoint endPoint)
    {
        var addr = new QuicAddr();
        var port = (ushort)endPoint.Port;
        var portNetworkOrder = (ushort)((port >> 8) | (port << 8));
        var bytes = endPoint.Address.GetAddressBytes();
        if (endPoint.AddressFamily == AddressFamily.InterNetworkV6) {
            addr.Family = (ushort)QUIC_ADDRESS_FAMILY_INET6;
            addr.Ipv6.sin6_port = portNetworkOrder;
            for (var i = 0; i < bytes.Length && i < 16; i++)
                addr.Ipv6.sin6_addr[i] = bytes[i];
        }
        else {
            addr.Family = (ushort)QUIC_ADDRESS_FAMILY_INET;
            addr.Ipv4.sin_port = portNetworkOrder;
            for (var i = 0; i < bytes.Length && i < 4; i++)
                addr.Ipv4.sin_addr[i] = bytes[i];
        }
        return addr;
    }

    private static unsafe void CloseHandles(IntPtr conn, IntPtr config, GCHandle gch)
    {
        var table = MsQuicApi.Table;
        if (conn != IntPtr.Zero) table->ConnectionClose((QUIC_HANDLE*)conn);
        if (config != IntPtr.Zero) table->ConfigurationClose((QUIC_HANDLE*)config);
        if (gch.IsAllocated) gch.Free();
    }

    private static unsafe IQuicConnection CreateConnection(IntPtr conn, IntPtr config, GCHandle gch,
        AndroidQuicConnectionState state, IPEndPoint remoteEndPoint) =>
        new AndroidQuicConnection((QUIC_HANDLE*)conn, (QUIC_HANDLE*)config, gch, state, remoteEndPoint);
}
