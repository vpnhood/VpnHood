using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Net.Toolkit.Monitoring;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Test.Device;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class VpnHoodClientFactoryTest : TestBase
{
    // The proxy connector is made before the client, which owns it only once made: a client that refused its options
    // left it open, a managed one with its store and its update
    [TestMethod]
    public async Task A_refused_client_closes_its_proxy_connector()
    {
        var proxyConnector = new DisposalRecordingProxyConnector();
        var clientFactory = new ProxyConnectorClientFactory(proxyConnector);
        using var vpnAdapter = new TestNullVpnAdapter();
        var clientOptions = new ClientOptions {
            AdapterName = "test", AccessKey = "test", ClientId = "test",
            MinPacketChannelTimespan = TimeSpan.FromMinutes(10),
            MaxPacketChannelTimespan = TimeSpan.FromMinutes(5)
        };

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => clientFactory.Create(
            new VpnHoodClientParams {
                ServiceOptions = new VpnServiceOptions { ClientOptions = clientOptions },
                VpnAdapter = vpnAdapter,
                SocketFactory = new TestSocketFactory(),
                ConfigFolder = TestHelper.WorkingPath,
                DataFolder = TestHelper.WorkingPath
            }, TestHelper.ClientNetFilter, TestCt));

        Assert.IsTrue(proxyConnector.IsDisposed);
    }

    private sealed class ProxyConnectorClientFactory(IProxyConnector proxyConnector) : VpnHoodClientFactory
    {
        protected override Task<IProxyConnector?> CreateProxyConnector(VpnHoodClientParams clientParams,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IProxyConnector?>(proxyConnector);
        }
    }

    // a connector that only tells whether it was closed
    private sealed class DisposalRecordingProxyConnector : IProxyConnector
    {
        public bool IsDisposed { get; private set; }
        public bool IsEnabled => true;
        public bool UseRecentSucceeded { get; set; }
        public ProgressStatus? Progress => null;
        public ProxyConnectorStatus Status => throw new NotSupportedException();

        public Task<ProxyConnection> ConnectAsync(ISocketFactory socketFactory, IPEndPoint ipEndPoint,
            string? hostName, RemoteCertificateValidationCallback? serverCertificateValidation, Action? onAttempt,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void RecordFailed(TcpClient tcpClient, Exception ex) => throw new NotSupportedException();
        public Task<bool> WaitForEndPoints(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CheckServers(ISocketFactory socketFactory, IPEndPoint ipEndPoint, string? hostName,
            RemoteCertificateValidationCallback? serverCertificateValidation, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateOptions(ProxyOptions proxyOptions, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task Flush(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return default;
        }
    }
}
