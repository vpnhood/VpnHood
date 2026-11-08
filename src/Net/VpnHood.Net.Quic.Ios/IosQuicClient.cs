using System.Threading.Channels;
using CoreFoundation;
using Microsoft.Extensions.Logging;
using Network;
using ObjCRuntime;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Quic.Ios;

/// <summary>
/// Provides the QUIC client for iOS, on Apple's Network.framework (iOS 15 or later): one multiplexed QUIC connection
/// whose streams are opened and accepted as with the desktop MsQuic client.
/// </summary>
/// <remarks>
/// Network.framework does the TLS handshake and evaluates the system's trust; then
/// <see cref="QuicClientConnectOptions.CertificateValidationCallback"/> decides. The callback gets the server's leaf
/// certificate, no chain, and either no error or <see cref="System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors"/>
/// when the system does not trust it; a callback that throws rejects the certificate. The client pins nothing itself.
/// </remarks>
/// <param name="initialMaxStreamDataBidirectionalLocal">The initial QUIC flow-control credit, in bytes, each stream this
/// client opens advertises, or <see langword="null"/> for Network.framework's default.</param>
/// <param name="initialMaxStreamDataBidirectionalRemote">The initial QUIC flow-control credit, in bytes, each stream the
/// server opens advertises, or <see langword="null"/> for Network.framework's default.</param>
/// <param name="initialMaxData">The initial QUIC flow-control credit, in bytes, the whole connection advertises, or
/// <see langword="null"/> for Network.framework's default. Network.framework grants more credit as it buffers data,
/// not as the app reads it, so these credits bound no memory. <see cref="IosSocketFactory"/> sets small ones.</param>
public sealed class IosQuicClient(
    ulong? initialMaxStreamDataBidirectionalLocal = null,
    ulong? initialMaxStreamDataBidirectionalRemote = null,
    ulong? initialMaxData = null)
    : IQuicClient
{
    /// <summary>
    /// Gets a value indicating whether Network.framework QUIC is supported: on iOS 15 or later. It is
    /// <see langword="false"/> on tvOS.
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsIOSVersionAtLeast(15);

    // DIAGNOSTIC counters (live QUIC stream count, in-flight send bytes, teardown timing, [VHQUIC] logs)
    // live in IosQuicDiagnostics — maintained only when that switch is on.

    /// <inheritdoc/>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="IOException">
    /// The connection failed: the endpoint could not be made, the server could not be reached, or the certificate was
    /// rejected.
    /// </exception>
    public async ValueTask<IQuicConnection> ConnectAsync(
        QuicClientConnectOptions options, CancellationToken cancellationToken)
    {
        var endpoint = NWEndpoint.Create(
            options.RemoteEndPoint.Address.ToString(),
            options.RemoteEndPoint.Port.ToString())
            ?? throw new IOException("Failed to create a QUIC endpoint.");
        var queue = new DispatchQueue("VpnHood.Quic.Ios");
        var multiplexGroup = new NWMultiplexGroup(endpoint);

        // QUIC parameters: ALPN "h3" (the desktop uses SslApplicationProtocol.Http3 purely as the ALPN
        // token — our QUIC is a custom transport, not HTTP/3) and the pinned-certificate verify bridge.
        using var parameters = NWParameters.CreateQuic(quicOptions => {
            // The .NET Network binding types this callback's argument as the base NWProtocolOptions even
            // though the underlying native object is a QUIC options block. A direct C# cast to
            // NWProtocolQuicOptions throws InvalidCastException (the managed wrapper is literally
            // NWProtocolOptions) — which, thrown on this native trampoline thread, aborts the
            // process with SIGABRT.
            // Re-wrap the SAME native handle as NWProtocolQuicOptions (owns:false — the parameters own
            // the native object); setters then mutate the real options block used by the connection.
            var quic = Runtime.GetINativeObject<NWProtocolQuicOptions>(quicOptions.Handle, owns: false)
                ?? throw new IOException("Failed to access QUIC protocol options.");
            quic.AddTlsApplicationProtocol("h3");
            quic.InitialMaxStreamsBidirectional = (ulong)options.MaxInboundBidirectionalStreams;

            // QUIC receive-window flow control (backpressure). Each window is applied only when the caller
            // configured it; leaving one null keeps Network.framework's (large) OS default for that window.
            // On iOS these are set to tight caps by the factory as the ~52 MB jetsam fix — without them the
            // server floods each stream and native receive buffering blows the limit. See IosSocketFactory.
            if (initialMaxStreamDataBidirectionalLocal.HasValue)
                quic.InitialMaxStreamDataBidirectionalLocal = initialMaxStreamDataBidirectionalLocal.Value;
            if (initialMaxStreamDataBidirectionalRemote.HasValue)
                quic.InitialMaxStreamDataBidirectionalRemote = initialMaxStreamDataBidirectionalRemote.Value;
            if (initialMaxData.HasValue)
                quic.InitialMaxData = initialMaxData.Value;

            using var secProtocolOptions = quic.SecProtocolOptions;
            IosQuicTls.Configure(secProtocolOptions, options.TargetHost,
                options.CertificateValidationCallback, queue);
        });

        var connectionGroup = new NWConnectionGroup(multiplexGroup, parameters);

        // Queue for peer-initiated (inbound) streams. The handler must be wired before the group starts
        // so no incoming stream is missed; IosQuicConnection.AcceptInboundStreamAsync drains it.
        var inboundStreams = Channel.CreateUnbounded<NWConnection>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });

        try {
            connectionGroup.SetNewConnectionHandler(stream => {
                if (inboundStreams.Writer.TryWrite(stream))
                    return;

                stream.TryCancel();
                stream.Dispose();
            });

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var reg = cancellationToken.Register(() => tcs.TrySetCanceled());

            connectionGroup.SetStateChangedHandler((state, error) => {
                switch (state) {
                    case NWConnectionGroupState.Ready:
                        tcs.TrySetResult();
                        break;
                    case NWConnectionGroupState.Failed:
                    case NWConnectionGroupState.Cancelled:
                        tcs.TrySetException(new IOException($"QUIC tunnel failed to connect: {error}"));
                        break;
                }
            });

            connectionGroup.SetQueue(queue);
            connectionGroup.Start();

            await tcs.Task.Vhc();
            connectionGroup.SetStateChangedHandler(null!);
            return new IosQuicConnection(connectionGroup, multiplexGroup, endpoint, queue,
                options.RemoteEndPoint, inboundStreams);
        }
        catch {
            // The server's control stream can arrive before setup is canceled. No connection wrapper
            // owns these queued native streams yet, so release them before closing the group.
            inboundStreams.Writer.TryComplete();
            while (inboundStreams.Reader.TryRead(out var pending)) {
                pending.TryCancel();
                VhInvoker.TryInvoke("dispose an unaccepted QUIC stream", LogLevel.Debug, pending.Dispose);
            }

            connectionGroup.TrySetStateChangedHandler(null);
            connectionGroup.TrySetNewConnectionHandler(null);
            connectionGroup.TryCancel();
            connectionGroup.Dispose();
            multiplexGroup.Dispose();
            endpoint.Dispose();
            VhInvoker.TryInvoke("dispose the dispatch queue", LogLevel.Debug, queue.Dispose);
            throw;
        }
    }
}
