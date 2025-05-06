namespace VpnHood.Core.Packets.VhPackets;

public static class IpPacketFactory
{
    public static VhIpPacket Create(Memory<byte> buffer)
    {
        if (buffer.Length < 1)
            throw new ArgumentException("Buffer too small to determine IP version.", nameof(buffer));

        var version = buffer.Span[0] >> 4;
        return version switch {
            4 => new VhIpV4Packet(buffer),
            6 => new VhIpV6Packet(buffer),
            _ => throw new NotSupportedException($"IP version {version} not supported."),
        };
    }
}