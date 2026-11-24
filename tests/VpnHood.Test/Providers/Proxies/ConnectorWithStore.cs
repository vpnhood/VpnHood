using VpnHood.Core.Proxies.Management;

namespace VpnHood.Test.Providers.Proxies;

internal sealed class ConnectorWithStore(ManagedProxyConnector connector, TestProxyEndPointStore store)
{
    public ManagedProxyConnector Connector { get; } = connector;
    public TestProxyEndPointStore Store { get; } = store;
}
