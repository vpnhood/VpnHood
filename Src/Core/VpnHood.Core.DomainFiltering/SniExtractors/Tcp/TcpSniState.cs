namespace VpnHood.Core.SniFiltering.SniExtractors.Tcp;

/// <summary>
/// State for TCP SNI extraction when data spans multiple packets.
/// </summary>
internal sealed class TcpSniState
{
    public byte[] Buffer { get; set; } = [];
    public int BufferLength { get; set; }
    public int PacketBudget { get; set; } = 3;
    public long DeadlineTicks { get; set; }
    public int MaxBytes { get; set; } = 16 * 1024; // TLS ClientHello is typically under 16KB
}
