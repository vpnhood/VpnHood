using VpnHood.Core.Filtering.Abstractions;

namespace VpnHood.Core.Filtering.DomainFiltering;

/// <summary>
/// Represents the domain filter's answer for a TCP connection, with the bytes read from its stream to find its name.
/// </summary>
public readonly struct StreamSniFilterResult
{
    /// <summary>Gets the verdict for the connection.</summary>
    public required FilterAction Action { get; init; }

    /// <summary>Gets the connection's domain name, or <see langword="null"/> when none was found.</summary>
    public required string? DomainName { get; init; }

    /// <summary>
    /// Gets the bytes read from the stream, which the caller must send on before the rest of it; empty when nothing
    /// was read.
    /// </summary>
    public required Memory<byte> ReadData { get; init; }

    internal static StreamSniFilterResult Passthrough() => new() {
        Action = FilterAction.Default,
        DomainName = null,
        ReadData = Memory<byte>.Empty
    };
}