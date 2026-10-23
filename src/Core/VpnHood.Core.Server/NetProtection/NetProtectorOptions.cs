using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Server.NetProtection;

internal class NetProtectorOptions
{
    public required int MaxUdpDnsPacketsPerSecond { get; init; }
    public required int MaxTcpConnectsPerSecond { get; init; }

    // NetScan is off unless both are set
    public required int? NetScanLimit { get; init; }
    public required TimeSpan? NetScanTimeout { get; init; }

    public required LogScope LogScope { get; init; }
}
