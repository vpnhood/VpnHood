namespace VpnHood.Net.PacketTransports;

/// <summary>
/// Provides a packet transport that queues sent packets and hands them to the derived class in batches, up to the
/// queue's capacity at a time, on a sending loop of its own.
/// </summary>
/// <param name="options">The queue's capacity, blocking and packet disposal.</param>
/// <exception cref="ArgumentOutOfRangeException">
/// The blocking timeout is negative or too long, or the queue's capacity is not above zero.
/// </exception>
public abstract class PacketTransport(PacketTransportOptions options) :
    PacketTransportBase(options, false, false);
