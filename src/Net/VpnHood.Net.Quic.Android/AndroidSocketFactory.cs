using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Net.Quic.Android;

/// <summary>
/// Provides a <see cref="SystemSocketFactory"/> that adds the Android QUIC client, <see cref="AndroidQuicClient"/>; it
/// makes TCP and UDP sockets as its base does.
/// </summary>
public class AndroidSocketFactory : SystemSocketFactory
{
    /// <summary>
    /// Gets a value indicating whether QUIC is supported, as <see cref="AndroidQuicClient.IsSupported"/> tells.
    /// </summary>
    public override bool IsQuicSupported => AndroidQuicClient.IsSupported;

    /// <summary>Creates an <see cref="AndroidQuicClient"/>.</summary>
    /// <returns>A new <see cref="AndroidQuicClient"/>.</returns>
    /// <exception cref="NotSupportedException">QUIC is not supported on this device.</exception>
    public override IQuicClient CreateQuicClient() => AndroidQuicClient.IsSupported
        ? new AndroidQuicClient()
        : throw new NotSupportedException("QUIC is not supported on this platform.");
}
