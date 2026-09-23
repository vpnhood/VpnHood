namespace VpnHood.Net.PacketTransports;

public abstract class PacketTransport(PacketTransportOptions options) :
    PacketTransportBase(options, false, false);