namespace VpnHood.Net.Quic.Abstractions;

/// <summary>
/// Represents a factory of the QUIC clients a platform provides, which an app hands to the code that connects.
/// </summary>
public interface IQuicClientFactory
{
    /// <summary>Gets a value indicating whether the platform can create QUIC clients.</summary>
    bool IsSupported { get; }

    /// <summary>Creates a QUIC client.</summary>
    /// <returns>A new QUIC client.</returns>
    /// <exception cref="NotSupportedException">The platform cannot create QUIC clients.</exception>
    IQuicClient CreateClient();
}
