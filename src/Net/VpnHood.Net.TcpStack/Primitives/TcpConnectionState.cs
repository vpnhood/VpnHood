namespace VpnHood.Net.TcpStack.Primitives;

internal enum TcpConnectionState
{
    SynReceived,
    Established,
    FinWait1,
    Closing,
    LastAck, // both FINs exchanged, ours not yet acknowledged: it is retransmitted until the ACK arrives
    Closed
}