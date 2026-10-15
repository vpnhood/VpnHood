using System.Net;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.TcpStack.Abstractions;

namespace VpnHood.Net.TcpStack.Test;

/// <summary>The packet, accept and wait helpers the stack's tests share.</summary>
internal static class TcpTestHarness
{
    public static IpPacket CreateTcpPacket(
        IPAddress srcIp, int srcPort,
        IPAddress dstIp, int dstPort,
        bool syn = false, bool ack = false, bool fin = false, bool psh = false, bool rst = false,
        uint seq = 0, uint ackNum = 0,
        byte[]? payload = null,
        ushort window = 65535,
        byte[]? options = null)
    {
        var packet = PacketBuilder.BuildTcp(
            new IPEndPoint(srcIp, srcPort),
            new IPEndPoint(dstIp, dstPort),
            options ?? ReadOnlySpan<byte>.Empty,
            payload ?? ReadOnlySpan<byte>.Empty);

        var tcp = packet.ExtractTcp();
        tcp.Synchronize = syn;
        tcp.Acknowledgment = ack;
        tcp.Finish = fin;
        tcp.Push = psh;
        tcp.Reset = rst;
        tcp.SequenceNumber = seq;
        tcp.AcknowledgmentNumber = ackNum;
        tcp.WindowSize = window;

        packet.UpdateAllChecksums();
        return packet;
    }

    public static async Task<ITcpClient> AcceptConnectionAsync(ITcpListener listener)
    {
        await foreach (var client in listener.AcceptAllAsync(CancellationToken.None))
            return client;
        throw new InvalidOperationException("No connection accepted");
    }

    public static async Task WaitForCondition(Func<bool> condition, CancellationToken ct, int timeoutMs = 1000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(5, ct);
    }
}
