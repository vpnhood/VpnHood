namespace VpnHood.Net.TcpStack.Primitives;

/// <summary>
/// The connection's states, after RFC 9293's: FinWait is FIN-WAIT-1 and FIN-WAIT-2 as one (our FIN is sent; once it is
/// acknowledged, the peer's FIN is awaited until the idle timeout), CloseWait is the peer's FIN before ours, LastAck
/// covers a simultaneous close too, and there is no TIME-WAIT (the README's Limits). Only SynReceived and Closed drive
/// logic.
/// </summary>
internal enum TcpConnectionState
{
    SynReceived,
    Established,
    FinWait,
    CloseWait,
    LastAck, // both FINs exchanged, ours not yet acknowledged: it is retransmitted until the ACK arrives
    Closed
}