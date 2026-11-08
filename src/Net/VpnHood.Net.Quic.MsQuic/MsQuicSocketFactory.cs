using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Net.Quic.MsQuic;

/// <summary>
/// A <see cref="SystemSocketFactory"/> that adds MsQuic-based QUIC client support on platforms
/// where MsQuic is available (Windows/Linux). TCP/UDP creation is inherited from the base factory.
/// </summary>
public class MsQuicSocketFactory : SystemSocketFactory
{
    /// <summary>
    /// Gets a value indicating whether MsQuic connections are supported on this platform.
    /// </summary>
    public override bool IsQuicSupported => MsQuicClient.IsSupported;

    /// <summary>
    /// Creates a QUIC client on MsQuic.
    /// </summary>
    /// <returns>A new <see cref="MsQuicClient"/>.</returns>
    /// <exception cref="NotSupportedException">MsQuic is not supported on this platform.</exception>
    public override IQuicClient CreateQuicClient() => MsQuicClient.IsSupported
        ? new MsQuicClient()
        : throw new NotSupportedException("QUIC is not supported on this platform.");
}
