using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Tunneling.Channels;

/// <summary>
/// Represents a channel of a session's tunnel: what carries its packets, or one forwarded TCP connection.
/// </summary>
/// <remarks>
/// Its graceful close, <see cref="IAsyncDisposable.DisposeAsync"/>, is its normal end, which hands a reusable connection
/// back to its pool; the quick one, <see cref="IDisposable.Dispose"/>, stops the channel at once and asks its
/// connection to close, with no reuse, without waiting for it.
/// </remarks>
public interface IChannel : IDisposable, IAsyncDisposable
{
    /// <summary>Gets the channel's ID.</summary>
    string ChannelId { get; }

    /// <summary>Gets when the channel last sent or received, in UTC.</summary>
    DateTime LastActivityTime { get; }

    /// <summary>Gets what the channel has sent and received.</summary>
    Traffic Traffic { get; }

    /// <summary>Starts the channel, which then reads from its peer.</summary>
    void Start();

    /// <summary>Gets the channel's state.</summary>
    PacketChannelState State { get; }
}