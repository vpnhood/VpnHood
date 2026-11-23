namespace VpnHood.Core.Filtering.DomainFiltering.SniExtractors.Tcp;

/// <summary>
/// Extracts SNI from TCP packets containing TLS ClientHello.
/// Handles reassembly when ClientHello spans multiple TCP segments.
/// </summary>
internal static class TcpSniExtractor
{
    private static readonly long TimeoutTicks300Ms = TimeSpan.FromMilliseconds(300).Ticks;

    /// <summary>
    /// Try to extract SNI from a TCP payload containing TLS data.
    /// </summary>
    /// <param name="tcpPayload">The TCP payload data.</param>
    /// <param name="state">State from previous call, or null for first packet.</param>
    /// <param name="nowTicks">Current time in ticks.</param>
    /// <returns>Extraction result with SNI, need-more, not-found or incomplete status.</returns>
    public static PacketSniResult TryExtractSniFromTcpPayload(
        ReadOnlySpan<byte> tcpPayload,
        object? state = null,
        long nowTicks = 0)
    {
        if (nowTicks == 0)
            nowTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;

        // the state of a ClientHello begun, or a new one: bytes that begin none are found out below, at once
        var tcpState = state as TcpSniState ?? new TcpSniState {
            PacketBudget = 3,
            DeadlineTicks = nowTicks + TimeoutTicks300Ms,
            MaxBytes = 16 * 1024
        };

        // Check budget/timeout. A ClientHello begun and given up is incomplete, which fails its flow closed, as a QUIC
        // one's does: given up as not found, it passed with its name unread
        if (tcpState.PacketBudget <= 0 || nowTicks > tcpState.DeadlineTicks)
            return PacketSniResult.Incomplete;

        tcpState.PacketBudget--;

        // Append new data to buffer
        var totalLength = tcpState.BufferLength + tcpPayload.Length;
        if (totalLength > tcpState.MaxBytes)
            return PacketSniResult.Incomplete; // Too much data

        if (totalLength > tcpState.Buffer.Length) {
            var newBuffer = new byte[Math.Min(totalLength * 2, tcpState.MaxBytes)];
            if (tcpState.BufferLength > 0)
                Buffer.BlockCopy(tcpState.Buffer, 0, newBuffer, 0, tcpState.BufferLength);
            tcpState.Buffer = newBuffer;
        }

        tcpPayload.CopyTo(tcpState.Buffer.AsSpan(tcpState.BufferLength));
        tcpState.BufferLength = totalLength;

        // The name, once all the records that hold the ClientHello are in, as the stream's reader has it
        // (StreamSniExtractor): bytes that begin no ClientHello have none, and one not well formed is incomplete
        var bufferSpan = tcpState.Buffer.AsSpan(0, tcpState.BufferLength);
        var recordsLength = TlsClientHelloParser.GetRecordsLength(bufferSpan);
        if (recordsLength == TlsClientHelloParser.NeverComplete)
            return PacketSniResult.Incomplete;

        if (bufferSpan.Length >= recordsLength) {
            if (!TlsClientHelloParser.TryGetServerNameFromRecords(bufferSpan, out var serverName))
                return PacketSniResult.Incomplete;

            return serverName != null ? PacketSniResult.Found(serverName) : PacketSniResult.NotFound;
        }

        // Need more data
        if (tcpState.PacketBudget <= 0 || nowTicks > tcpState.DeadlineTicks)
            return PacketSniResult.Incomplete;

        return PacketSniResult.Pending(tcpState);
    }
}
