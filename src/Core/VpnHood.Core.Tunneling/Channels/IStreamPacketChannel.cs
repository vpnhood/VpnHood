namespace VpnHood.Core.Tunneling.Channels;

/// <summary>
/// Represents a packet channel that is a connection, the kind a tunnel's channel manager takes: a session has several,
/// picked by flow.
/// </summary>
/// <remarks>
/// Each closes with a grace, in which what is on its way still arrives, unless the channel manager, holding too many
/// closing ones, ends it sooner. One of a developer's own must speak the close message and the grace its peer expects; a
/// new transport goes through <see cref="Connections.IStreamConnection"/> instead.
/// </remarks>
public interface IStreamPacketChannel : IPacketChannel
{
    /// <summary>Gets the time of the request that made the channel, by the client's clock.</summary>
    DateTime RequestTime { get; }

    /// <summary>
    /// Gets when the channel's close began, in UTC, or <see langword="null"/> while it is open; the channel manager lets
    /// the oldest closes go first.
    /// </summary>
    DateTime? CloseTime { get; }

    /// <summary>
    /// Starts the channel's close: it is <see cref="PacketChannelState.Disconnecting"/> at once, tells its peer, and
    /// goes after its grace, or sooner where the channel manager ends it. One already closing or gone is left as it is.
    /// </summary>
    void Close();
}
