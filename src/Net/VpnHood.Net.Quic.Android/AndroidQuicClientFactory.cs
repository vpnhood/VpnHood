using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Net.Quic.Android;

/// <summary>
/// Provides the Android QUIC client, <see cref="AndroidQuicClient"/>.
/// </summary>
public class AndroidQuicClientFactory : IQuicClientFactory
{
    /// <summary>
    /// Gets a value indicating whether QUIC is supported, as <see cref="AndroidQuicClient.IsSupported"/> tells.
    /// </summary>
    public bool IsSupported => AndroidQuicClient.IsSupported;

    /// <summary>Creates an <see cref="AndroidQuicClient"/>.</summary>
    /// <returns>A new <see cref="AndroidQuicClient"/>.</returns>
    /// <exception cref="NotSupportedException">QUIC is not supported on this device.</exception>
    public IQuicClient CreateClient() => AndroidQuicClient.IsSupported
        ? new AndroidQuicClient()
        : throw new NotSupportedException("QUIC is not supported on this platform.");
}
