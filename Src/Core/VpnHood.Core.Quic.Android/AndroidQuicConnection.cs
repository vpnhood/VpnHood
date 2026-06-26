using System.Net;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Quic;
using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Quic.Droid.Interop;
using VpnHood.Core.Toolkit.Logging;
using static Microsoft.Quic.MsQuic;

namespace VpnHood.Core.Quic.Droid;

/// <summary>
/// A QUIC connection backed by MsQuic (libmsquic.so) via our own P/Invoke bindings — bypassing
/// System.Net.Quic entirely (whose certificate validation is incompatible with Android's crypto backend).
/// Pointer work is confined to <c>unsafe</c> members so the async members compile.
/// </summary>
internal sealed class AndroidQuicConnection : IQuicConnection
{
    private unsafe QUIC_HANDLE* _connection;
    private unsafe QUIC_HANDLE* _configuration;
    private GCHandle _gch;
    private readonly AndroidQuicConnectionState _state;
    private bool _disposed;

    public IPEndPoint RemoteEndPoint { get; }
    public IPEndPoint LocalEndPoint { get; }

    public unsafe AndroidQuicConnection(QUIC_HANDLE* connection, QUIC_HANDLE* configuration, GCHandle gch,
        AndroidQuicConnectionState state, IPEndPoint remoteEndPoint)
    {
        _connection = connection;
        _configuration = configuration;
        _gch = gch;
        _state = state;
        RemoteEndPoint = remoteEndPoint;
        // MsQuic does not cheaply surface the local UDP endpoint; VpnHood uses these only for diagnostics.
        LocalEndPoint = new IPEndPoint(
            remoteEndPoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? IPAddress.IPv6Any : IPAddress.Any, 0);
    }

    public unsafe ValueTask<Stream> OpenOutboundStreamAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = AndroidQuicStream.OpenOutbound(_connection);
        return new ValueTask<Stream>(stream);
    }

    public async ValueTask<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken)
    {
        // Peer-initiated streams are queued by the connection callback. Throws ChannelClosedException
        // once the connection is disposed (ending any accept loop), matching the desktop client.
        return await _state.InboundStreams.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    // Routes a connection event to managed state. Static (UnmanagedCallersOnly) -> resolves state via ctx.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe int ConnectionCallback(QUIC_HANDLE* connection, void* ctx, QUIC_CONNECTION_EVENT* evt)
    {
        var state = (AndroidQuicConnectionState)GCHandle.FromIntPtr((IntPtr)ctx).Target!;
        try {
            switch (evt->Type) {
                case QUIC_CONNECTION_EVENT_TYPE.CONNECTED:
                    state.Connected.TrySetResult();
                    break;

                case QUIC_CONNECTION_EVENT_TYPE.PEER_CERTIFICATE_RECEIVED:
                    return ValidateCertificate(state, evt);

                case QUIC_CONNECTION_EVENT_TYPE.PEER_STREAM_STARTED:
                    var stream = AndroidQuicStream.FromInbound(evt->PEER_STREAM_STARTED.Stream);
                    state.InboundStreams.Writer.TryWrite(stream);
                    break;

                case QUIC_CONNECTION_EVENT_TYPE.SHUTDOWN_INITIATED_BY_TRANSPORT:
                    state.Connected.TrySetException(new IOException(
                        $"QUIC connection failed. status=0x{(uint)evt->SHUTDOWN_INITIATED_BY_TRANSPORT.Status:x}"));
                    break;

                case QUIC_CONNECTION_EVENT_TYPE.SHUTDOWN_INITIATED_BY_PEER:
                    state.Connected.TrySetException(new IOException(
                        $"QUIC connection rejected by peer. error=0x{evt->SHUTDOWN_INITIATED_BY_PEER.ErrorCode:x}"));
                    break;

                case QUIC_CONNECTION_EVENT_TYPE.SHUTDOWN_COMPLETE:
                    state.Connected.TrySetException(new IOException("QUIC connection shut down."));
                    state.Shutdown.TrySetResult();
                    state.InboundStreams.Writer.TryComplete();
                    break;
            }
        }
        catch {
            return QUIC_STATUS_INTERNAL_ERROR;
        }
        return QUIC_STATUS_SUCCESS;
    }

    // We requested USE_PORTABLE_CERTIFICATES, so the peer cert arrives as DER in a QUIC_BUFFER. We build
    // an Android-backed X509Certificate2 (no OpenSSL), compute the real SslPolicyErrors (chain + hostname,
    // exactly like the TCP/SslStream path), and run VpnHood's validation callback (which accepts a
    // CA-valid cert when errors==None, or a pinned self-signed cert by hash otherwise).
    private static unsafe int ValidateCertificate(AndroidQuicConnectionState state, QUIC_CONNECTION_EVENT* evt)
    {
        var certPtr = (QUIC_BUFFER*)evt->PEER_CERTIFICATE_RECEIVED.Certificate;
        if (certPtr == null || certPtr->Length == 0) {
            VhLogger.Instance.LogWarning("AndroidQuic: no peer certificate received.");
            return QUIC_STATUS_BAD_CERTIFICATE;
        }

        bool valid;
        try {
            var der = new ReadOnlySpan<byte>(certPtr->Buffer, (int)certPtr->Length).ToArray();
            using var cert = X509CertificateLoader.LoadCertificate(der);
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

            // Add the intermediate certs msquic provides (portable Chain buffer, usually a PKCS#7 bundle)
            // so the chain can actually be built — otherwise it always reports RemoteCertificateChainErrors.
            var intermediateCount = 0;
            var chainPtr = (QUIC_BUFFER*)evt->PEER_CERTIFICATE_RECEIVED.Chain;
            if (chainPtr != null && chainPtr->Length > 0) {
                try {
                    var chainBytes = new ReadOnlySpan<byte>(chainPtr->Buffer, (int)chainPtr->Length).ToArray();
                    var extra = new X509Certificate2Collection();
                    // The portable Chain buffer is a PKCS#7 / concatenated-DER bundle; X509CertificateLoader
                    // has no multi-certificate importer, so the (obsolete) Import is still the right call here.
#pragma warning disable SYSLIB0057
                    extra.Import(chainBytes);
#pragma warning restore SYSLIB0057
                    chain.ChainPolicy.ExtraStore.AddRange(extra);
                    intermediateCount = extra.Count;
                }
                catch (Exception ex) {
                    VhLogger.Instance.LogWarning(ex, "AndroidQuic: failed to parse peer chain bundle.");
                }
            }

            var errors = SslPolicyErrors.None;
            var chainOk = chain.Build(cert);
            if (!chainOk)
                errors |= SslPolicyErrors.RemoteCertificateChainErrors;
            var nameOk = string.IsNullOrEmpty(state.TargetHost) || cert.MatchesHostname(state.TargetHost);
            if (!nameOk)
                errors |= SslPolicyErrors.RemoteCertificateNameMismatch;

            valid = state.CertificateValidationCallback(state, cert, chain, errors);
            VhLogger.Instance.LogInformation(
                "AndroidQuic cert. Subject:{Subject}, TargetHost:{Host}, Thumbprint:{Thumb}, DerLen:{Len}, " +
                "ChainLen:{ChainLen}, Intermediates:{Inter}, ChainOk:{ChainOk}, NameOk:{NameOk}, Errors:{Errors}, Valid:{Valid}",
                cert.Subject, state.TargetHost, cert.Thumbprint, der.Length,
                chainPtr == null ? 0 : (int)chainPtr->Length, intermediateCount, chainOk, nameOk, errors, valid);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "AndroidQuic: certificate validation threw.");
            valid = false;
        }
        return valid ? QUIC_STATUS_SUCCESS : QUIC_STATUS_BAD_CERTIFICATE;
    }

    public unsafe ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return ValueTask.CompletedTask;

        _state.InboundStreams.Writer.TryComplete();
        while (_state.InboundStreams.Reader.TryRead(out var stream)) {
            try { stream.Dispose(); } catch { /* ignore */ }
        }
        if (_connection != null) {
            try { MsQuicApi.Table->ConnectionShutdown(_connection, QUIC_CONNECTION_SHUTDOWN_FLAGS.NONE, 0); } catch { /* ignore */ }
            MsQuicApi.Table->ConnectionClose(_connection); // blocks until callbacks drained
            _connection = null;
        }
        if (_configuration != null) {
            MsQuicApi.Table->ConfigurationClose(_configuration);
            _configuration = null;
        }
        if (_gch.IsAllocated) _gch.Free();
        return ValueTask.CompletedTask;
    }
}
