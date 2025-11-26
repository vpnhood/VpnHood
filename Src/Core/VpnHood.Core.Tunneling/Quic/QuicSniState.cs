namespace VpnHood.Core.Tunneling.Quic;

// ReSharper disable IdentifierTypo
// ReSharper disable GrammarMistakeInComment
// ReSharper disable FieldCanBeMadeReadOnly.Global
public sealed class QuicSniState
{
    // Secrets
    public bool IsV2;
    public bool SecretsReady;
    public byte[] Dcid = [];
    public byte[] Key = [];
    public byte[] Iv = [];
    public byte[] Hp = [];

    // Reassembly
    // We keep sparse segments (offset, data). We assemble contiguous [0..N) when trying to parse.
    public List<(ulong Off, byte[] Data)> Segments = [];

    // Budgets
    public int PacketBudget = 3; // how many client Initial packets to consider
    public long DeadlineTicks; // e.g., now + 300ms
    public int MaxBytes = 64 * 1024; // safety cap
}