namespace VpnHood.Core.Tunneling.Channels;

/// <summary>Specifies a channel's state.</summary>
public enum PacketChannelState
{
    /// <summary>The channel is not started yet.</summary>
    NotStarted,

    /// <summary>The channel is started, and carries what it is given.</summary>
    Connected,

    /// <summary>
    /// The channel's close has begun: the tunnel gives it no more, and it goes after its grace, or sooner where the
    /// channel manager ends it.
    /// </summary>
    Disconnecting,

    /// <summary>The channel is gone.</summary>
    Disposed
}