using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Net.Quic.MsQuic;

/// <summary>
/// Provides the MsQuic QUIC client, <see cref="MsQuicClient"/>, on the platforms where MsQuic is available
/// (Windows and Linux).
/// </summary>
public class MsQuicClientFactory : IQuicClientFactory
{
    /// <summary>
    /// Gets a value indicating whether MsQuic connections are supported on this platform.
    /// </summary>
    public bool IsSupported => MsQuicClient.IsSupported;

    /// <summary>
    /// Creates a QUIC client on MsQuic.
    /// </summary>
    /// <returns>A new <see cref="MsQuicClient"/>.</returns>
    /// <exception cref="NotSupportedException">MsQuic is not supported on this platform.</exception>
    public IQuicClient CreateClient() => MsQuicClient.IsSupported
        ? new MsQuicClient()
        : throw new NotSupportedException("QUIC is not supported on this platform.");
}
