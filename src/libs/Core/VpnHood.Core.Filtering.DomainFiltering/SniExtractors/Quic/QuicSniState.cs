namespace VpnHood.Core.Filtering.DomainFiltering.SniExtractors.Quic;

// ReSharper disable IdentifierTypo
// ReSharper disable GrammarMistakeInComment
// ReSharper disable FieldCanBeMadeReadOnly.Global
internal sealed class QuicSniState
{
    // Secrets, made with the state from the connection ID of the flow's first Initial
    public bool IsV2;
    public byte[] Dcid = [];
    public byte[] Key = [];
    public byte[] Iv = [];
    public byte[] Hp = [];

    // Reassembly
    // We keep sparse segments (offset, data). We assemble contiguous [0..N) when trying to parse.
    public List<(ulong Off, byte[] Data)> Segments = [];

    // Budgets
    public int PacketBudget; // how many datagrams with a client Initial may still come
    public long DeadlineTicks; // the first datagram's time and the name's timeout
    public int MaxBytes = 64 * 1024; // safety cap
}