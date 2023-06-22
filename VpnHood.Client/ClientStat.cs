using VpnHood.Client.ConnectorServices;

namespace VpnHood.Client;

public class ClientStat
{
    public required ConnectorStat ConnectorStat { get; init; }
}