namespace VpnHood.Core.TcpStack.Primitives;

internal enum TcpConnectionState
{
    SynReceived,
    Established,
    FinWait1,
    Closing,
    Closed
}