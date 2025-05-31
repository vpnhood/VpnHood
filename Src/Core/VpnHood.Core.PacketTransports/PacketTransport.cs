namespace VpnHood.Core.PacketTransports;

public abstract class PacketTransport(PacketTransportOptions options) : 
    PacketTransportBase(options, false, false);
