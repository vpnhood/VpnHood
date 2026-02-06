namespace VpnHood.Core.DomainFiltering.SniExtractors.Tcp;

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
    /// <returns>Extraction result with SNI, need-more, or not-found status.</returns>
    public static SniExtractionResult TryExtractSniFromTcpPayload(
        ReadOnlySpan<byte> tcpPayload,
        object? state = null,
        long nowTicks = 0)
    {
        if (nowTicks == 0) 
            nowTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;

        var tcpState = state as TcpSniState;

        // First packet - check if it looks like TLS ClientHello
        if (tcpState == null) {
            if (!LooksLikeClientHello(tcpPayload))
                return SniExtractionResult.NotFound;

            tcpState = new TcpSniState {
                PacketBudget = 3,
                DeadlineTicks = nowTicks + TimeoutTicks300Ms,
                MaxBytes = 16 * 1024
            };
        }

        // Check budget/timeout
        if (tcpState.PacketBudget <= 0 || nowTicks > tcpState.DeadlineTicks)
            return SniExtractionResult.NotFound;

        tcpState.PacketBudget--;

        // Append new data to buffer
        var totalLength = tcpState.BufferLength + tcpPayload.Length;
        if (totalLength > tcpState.MaxBytes)
            return SniExtractionResult.NotFound; // Too much data

        if (totalLength > tcpState.Buffer.Length) {
            var newBuffer = new byte[Math.Min(totalLength * 2, tcpState.MaxBytes)];
            if (tcpState.BufferLength > 0)
                Buffer.BlockCopy(tcpState.Buffer, 0, newBuffer, 0, tcpState.BufferLength);
            tcpState.Buffer = newBuffer;
        }

        tcpPayload.CopyTo(tcpState.Buffer.AsSpan(tcpState.BufferLength));
        tcpState.BufferLength = totalLength;

        // Try to parse SNI from accumulated data
        var bufferSpan = tcpState.Buffer.AsSpan(0, tcpState.BufferLength);
        var sni = TlsClientHelloParser.ExtractSni(bufferSpan, hasRecordHeader: true);
        
        if (sni != null)
            return SniExtractionResult.Found(sni);

        // Check if we have enough data to determine there's no SNI
        if (HasCompleteClientHello(bufferSpan))
            return SniExtractionResult.NotFound;

        // Need more data
        if (tcpState.PacketBudget <= 0 || nowTicks > tcpState.DeadlineTicks)
            return SniExtractionResult.NotFound;

        return SniExtractionResult.Pending(tcpState);
    }

    private static bool LooksLikeClientHello(ReadOnlySpan<byte> data)
    {
        // Minimum TLS record: ContentType(1) + Version(2) + Length(2) + Handshake header(4)
        if (data.Length < 9)
            return false;

        // Check TLS Handshake record type
        if (data[0] != 0x16)
            return false;

        // Check handshake message type is ClientHello
        if (data[5] != 0x01)
            return false;

        return true;
    }

    private static bool HasCompleteClientHello(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5)
            return false;

        // Get TLS record length
        var recordLength = (data[3] << 8) | data[4];
        var totalExpected = 5 + recordLength;

        return data.Length >= totalExpected;
    }
}
