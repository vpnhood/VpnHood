using Ga4.Trackers;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client;

internal class ClientSessionOptions
{
    public required IVpnAdapter VpnAdapter { get; init; }
    public required ISocketFactory SocketFactory { get; init; }
    public required ITracker? Tracker { get; init; }
    public required AccessUsage AccessUsage { get; init; }
    public required ConnectorService ConnectorService { get; init; }
    public required DomainFilteringService DomainFilteringService { get; init; }
    public required NetFilter NetFilter { get; init; }
    public required ChannelProtocol ChannelProtocol { get; init; }
    public required bool DropUdp { get; init; }
    public required bool DropQuic { get; init; }
    public required bool UseTcpProxy { get; init; }
}