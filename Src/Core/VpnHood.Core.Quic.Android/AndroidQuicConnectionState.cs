using System.Net.Security;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace VpnHood.Core.Quic.Droid;

/// <summary>
/// Shared state for a connection's unmanaged callback. Lives behind a <see cref="GCHandle"/> that is
/// passed to MsQuic as the callback context, so the static callback can route events to managed state.
/// </summary>
internal sealed class AndroidQuicConnectionState
{
    public readonly TaskCompletionSource Connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource Shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly Channel<IntPtr> InboundStreams =
        Channel.CreateUnbounded<IntPtr>(new UnboundedChannelOptions { SingleWriter = true });

    public required RemoteCertificateValidationCallback CertificateValidationCallback { get; init; }
    public required string TargetHost { get; init; }
}