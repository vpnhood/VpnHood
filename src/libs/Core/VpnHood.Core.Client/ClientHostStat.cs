namespace VpnHood.Core.Client;

internal interface IClientHostStat
{
    int TcpTunnelledCount { get; }
    int TcpPassthruCount { get; }
}