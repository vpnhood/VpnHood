namespace VpnHood.Core.Packets.Transports;

public abstract class PacketTransport(PacketTransportOptions options) : 
    PacketTransportBase(options, false, false);
