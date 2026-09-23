namespace VpnHood.Net.TcpStack.Primitives;

internal enum TcpConnectionState
{
    SynReceived,
    Established,
    FinWait1,
    Closing,
    Closed
}