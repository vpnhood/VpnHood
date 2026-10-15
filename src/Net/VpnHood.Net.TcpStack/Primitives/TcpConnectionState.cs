namespace VpnHood.Net.TcpStack.Primitives;

/// <summary>
/// The connection's states, named loosely after RFC 9293's: FinWait1 stays once our FIN is acknowledged (FIN-WAIT-2: the
/// peer's FIN is awaited until the idle timeout), Closing is the peer's FIN before ours (CLOSE-WAIT), LastAck covers a
/// simultaneous close too, and there is no TIME-WAIT (the README's Limits). Only SynReceived and Closed drive logic.
/// </summary>
internal enum TcpConnectionState
{
    SynReceived,
    Established,
    FinWait1,
    Closing,
    LastAck, // both FINs exchanged, ours not yet acknowledged: it is retransmitted until the ACK arrives
    Closed
}