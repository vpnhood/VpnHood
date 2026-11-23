using System.Text.Json;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Core.Client.VpnServices.Host.Sessions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Test.Device;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class VpnServiceHostTest : TestBase
{
    // A session's end counted as written before its write, so one whose write failed, as where the file was held,
    // barred the next, and the status file never showed the end. The next is written
    [TestMethod]
    public async Task A_failed_write_of_a_sessions_end_leaves_the_next_to_be_written()
    {
        var context = new VpnServiceContext(CreateFolder());
        var sessionId = context.BeginSession();

        await using (new FileStream(context.StatusFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            Assert.IsFalse(await context.TryWriteConnectionInfo(sessionId, CreateEnd(), TestCt));

        Assert.IsTrue(await context.TryWriteConnectionInfo(sessionId, CreateEnd(), TestCt));
        var written = JsonSerializer.Deserialize<ConnectionInfo>(
            await File.ReadAllTextAsync(context.StatusFilePath, TestCt));

        Assert.AreEqual(ClientState.Disposed, written?.ClientState);
    }

    // A session whose end could not be written was open to any word after it, so a Connected the client had sent
    // before its end replaced it, in the state the app reads and then on disk. The end stands; only it is written again
    [TestMethod]
    public async Task A_session_whose_end_could_not_be_written_takes_no_older_word()
    {
        var context = new VpnServiceContext(CreateFolder());
        var sessionId = context.BeginSession();

        await using (new FileStream(context.StatusFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None)) {
            Assert.IsFalse(await context.TryWriteConnectionInfo(sessionId, CreateEnd(), TestCt));
            Assert.IsFalse(await context.TryWriteConnectionInfo(sessionId, CreateInfo(ClientState.Connected), TestCt));
            Assert.AreEqual(ClientState.Disposed, context.ConnectionInfo.ClientState);
        }

        Assert.IsFalse(await context.TryWriteConnectionInfo(sessionId, CreateInfo(ClientState.Connected), TestCt));
        Assert.IsTrue(await context.TryWriteConnectionInfo(sessionId, CreateEnd(), TestCt));
        var written = JsonSerializer.Deserialize<ConnectionInfo>(
            await File.ReadAllTextAsync(context.StatusFilePath, TestCt));

        Assert.AreEqual(ClientState.Disposed, written?.ClientState);
    }

    // An answer to the app between a new session's begin and its first word told the last session's word, its end say,
    // as this one's: a stop asked then took it for the end at once. A session with no word yet reads as initializing
    [TestMethod]
    public async Task A_session_with_no_word_yet_reads_as_initializing()
    {
        var context = new VpnServiceContext(CreateFolder());
        var sessionId = context.BeginSession();
        Assert.IsTrue(await context.TryWriteConnectionInfo(sessionId, CreateEnd(), TestCt));

        context.BeginSession();
        Assert.AreEqual(ClientState.Initializing, context.ConnectionInfo.ClientState);
    }

    // A session's end whose write failed was replaced by a later end of the session, so a stop's reason, a revoke's say,
    // was lost where an end with none came next and was written, by itself or by an answer's retry. The first end
    // stands, and is the one written again
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_sessions_first_end_stands_through_its_later_ends(bool isRetriedByAnswer)
    {
        var context = new VpnServiceContext(CreateFolder());
        var sessionId = context.BeginSession();

        await using (new FileStream(context.StatusFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None)) {
            Assert.IsFalse(await context.TryWriteConnectionInfo(sessionId,
                CreateInfo(ClientState.Disposed, new VpnServiceRevokedException()), TestCt));

            Assert.IsFalse(await context.TryWriteConnectionInfo(sessionId, CreateEnd(), TestCt));
        }

        Assert.IsTrue(isRetriedByAnswer
            ? await context.TryRewriteEnd(sessionId, TestCt)
            : await context.TryWriteConnectionInfo(sessionId, CreateEnd(), TestCt));

        var written = JsonSerializer.Deserialize<ConnectionInfo>(
            await File.ReadAllTextAsync(context.StatusFilePath, TestCt));

        Assert.AreEqual(ClientState.Disposed, written?.ClientState);
        Assert.IsTrue(written?.Error?.Is<VpnServiceRevokedException>(), "The first end's reason was lost.");
    }

    // A proxies' reset kept from before the session was lost where the next reconfigure came once the session was up,
    // or where applying the kept one failed. The next one carries it, the rest of its own as it came
    [TestMethod]
    public void A_kept_reconfigures_proxies_reset_goes_on_with_the_next()
    {
        var nextParams = ClientReconfigurer.WithKeptReset(CreateReconfigureParams(resetStates: true),
            CreateReconfigureParams(resetStates: false, proxyMode: ProxyMode.Simple));

        Assert.IsTrue(nextParams.ProxyOptions.ResetStates);
        Assert.AreEqual(ProxyMode.Simple, nextParams.ProxyOptions.Mode);

        // none kept, or one with no reset: the next one is as it came
        Assert.IsFalse(ClientReconfigurer.WithKeptReset(null, CreateReconfigureParams(resetStates: false))
            .ProxyOptions.ResetStates);

        Assert.IsFalse(ClientReconfigurer.WithKeptReset(CreateReconfigureParams(resetStates: false),
            CreateReconfigureParams(resetStates: false)).ProxyOptions.ResetStates);
    }

    private static ClientReconfigureParams CreateReconfigureParams(bool resetStates,
        ProxyMode proxyMode = ProxyMode.Managed)
    {
        return new ClientReconfigureParams {
            DropQuic = false,
            DropUdp = false,
            UseTcpForwarding = false,
            AllowAnonymousTracker = false,
            ChannelProtocol = ChannelProtocol.Tcp,
            UnroutedIpMode = SplitUnsupportedIpMode.Block,
            UnsupportedIpV6Mode = SplitUnsupportedIpMode.Block,
            UseKillSwitch = false,
            ProxyOptions = new ProxyOptions { Mode = proxyMode, ResetStates = resetStates }
        };
    }

    // A derived factory's mapper that failed left the domain filter open, which holds its split database
    [TestMethod]
    public async Task A_filter_whose_mapper_fails_closes_its_domain_filter()
    {
        var domainFilter = new DisposalRecordingDomainFilter();
        using var vpnAdapter = new TestNullVpnAdapter();
        var folder = CreateFolder();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => VpnServiceClientBuilder.BuildNetFilter(
            new FailingMapperClientFactory(domainFilter),
            new VpnHoodClientParams {
                ServiceOptions = new VpnServiceOptions {
                    ClientOptions = new ClientOptions { AdapterName = "test", AccessKey = "test", ClientId = "test" }
                },
                VpnAdapter = vpnAdapter,
                SocketFactory = new TestSocketFactory(),
                ConfigFolder = folder,
                DataFolder = folder
            }, TestCt));

        Assert.IsTrue(domainFilter.IsDisposed);
    }

    private string CreateFolder()
    {
        var folder = Path.Combine(TestHelper.WorkingPath, nameof(VpnServiceHostTest) + "_" + Guid.CreateVersion7());
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static ConnectionInfo CreateEnd()
    {
        return CreateInfo(ClientState.Disposed);
    }

    private static ConnectionInfo CreateInfo(ClientState clientState, Exception? error = null)
    {
        return new ConnectionInfo {
            ClientState = clientState,
            CreatedTime = DateTime.UtcNow,
            ClientStateChangedTime = null,
            ClientStateProgress = null,
            ProxyConnectorStatus = null,
            SessionInfo = null,
            SessionStatus = null,
            Error = error?.ToApiError()
        };
    }

    private sealed class FailingMapperClientFactory(IDomainFilter domainFilter) : VpnHoodClientFactory
    {
        protected override IDomainFilter CreateInnerDomainFilter(VpnHoodClientParams clientParams) => domainFilter;

        public override IIpMapper CreateIpMapper(VpnHoodClientParams clientParams) =>
            throw new InvalidOperationException("The mapper failed.");
    }

    // a filter that only tells whether it was closed
    private sealed class DisposalRecordingDomainFilter : IDomainFilter
    {
        public bool IsDisposed { get; private set; }
        public bool IsEmpty => true;
        public event EventHandler? Changed { add { } remove { } }
        public FilterAction Process(string? domainName) => FilterAction.Default;
        public Task Reconfigure(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() => IsDisposed = true;
    }
}
