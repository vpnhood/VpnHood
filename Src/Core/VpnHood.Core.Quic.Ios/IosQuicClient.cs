using System.Threading.Channels;
using CoreFoundation;
using Network;
using ObjCRuntime;
using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Quic.Ios;

/// <summary>
/// Client-side QUIC connector for iOS, backed by Apple's Network.framework (iOS 15+). Establishes a
/// multiplexed QUIC tunnel and exposes lightweight streams over it, matching the desktop MsQuic client.
/// </summary>
public sealed class IosQuicClient : IQuicClient
{
    public static bool IsSupported => OperatingSystem.IsIOSVersionAtLeast(15);

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
        var parameters = NWParameters.CreateQuic(quicOptions => {
            // The .NET Network binding types this callback's argument as the base NWProtocolOptions even
            // though the underlying native object is a QUIC options block. A direct C# cast to
            // NWProtocolQuicOptions throws InvalidCastException (the managed wrapper is literally
            // NWProtocolOptions) — which, thrown on this native trampoline thread, SIGABRTs the process.
            // Re-wrap the SAME native handle as NWProtocolQuicOptions (owns:false — the parameters own
            // the native object); setters then mutate the real options block used by the connection.
            var quic = Runtime.GetINativeObject<NWProtocolQuicOptions>(quicOptions.Handle, owns: false)
                ?? throw new IOException("Failed to access QUIC protocol options.");
            quic.AddTlsApplicationProtocol("h3");
            quic.InitialMaxStreamsBidirectional = (ulong)options.MaxInboundBidirectionalStreams;
            IosQuicTls.Configure(quic.SecProtocolOptions, options.TargetHost,
                options.CertificateValidationCallback, queue);
        });

        var connectionGroup = new NWConnectionGroup(multiplexGroup, parameters);

        // Queue for peer-initiated (inbound) streams. The handler must be wired before the group starts
        // so no incoming stream is missed; IosQuicConnection.AcceptInboundStreamAsync drains it.
        var inboundStreams = Channel.CreateUnbounded<NWConnection>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });

        try {
            connectionGroup.SetNewConnectionHandler(stream => inboundStreams.Writer.TryWrite(stream));

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
            return new IosQuicConnection(connectionGroup, multiplexGroup, endpoint, queue,
                options.RemoteEndPoint, inboundStreams);
        }
        catch {
            inboundStreams.Writer.TryComplete();
            try { connectionGroup.Cancel(); } catch { /* ignore */ }
            connectionGroup.Dispose();
            multiplexGroup.Dispose();
            endpoint.Dispose();
            throw;
        }
    }
}
