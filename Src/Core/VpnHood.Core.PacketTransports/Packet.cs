namespace VpnHood.Core.PacketTransports;

public abstract class Packet(PacketTransportOptions options) : 
    PacketTransportBase(options, false, false);
