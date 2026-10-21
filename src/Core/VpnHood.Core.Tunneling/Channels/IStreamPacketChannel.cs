namespace VpnHood.Core.Tunneling.Channels;

// A packet channel that is a connection, the kind a tunnel's channel manager takes: a session has several, picked by
// flow, and each closes with a grace, in which what is on its way still arrives. One of a developer's own must speak
// the close message and the grace its peer expects; a new transport goes through IStreamConnection instead
public interface IStreamPacketChannel : IPacketChannel
{
    // the time of the request that made it, by the client's clock
    DateTime RequestTime { get; }

    // when its close began, null while open: the manager lets the oldest closes go first
    DateTime? CloseTime { get; }

    // starts its close: it is Disconnecting at once, tells the peer, and goes after its grace; one already closing or
    // gone is left as it is
    void Close();
}
