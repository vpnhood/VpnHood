using System.Net;
using System.Text;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Net.TcpStack.Test;

// VpnHood.Net.TcpStack's README is a copy of the Snippet regions here, made by pub/lib/Update-Snippets.ps1; the
// NuGet publish fails when a copy differs. Edit a region, then run the script.
[TestClass]
public sealed class TcpStackReadmeExampleTest
{
    public required TestContext TestContext { get; set; }

    private static readonly IPAddress HostIp = IPAddress.Parse("10.0.0.2");
    private static readonly IPAddress ServerIp = IPAddress.Parse("11.0.0.1");
    private const int HostPort = 50_000;
    private const int ServerPort = 8080;

    // The README's quick start and echo server, run as they are: the host connects to the virtual endpoint through
    // the adapter, sends a line, and gets it back.
    [TestMethod]
    [Timeout(30_000)]
    public async Task Echo_server_answers_the_host()
    {
        using var adapter = new TestPacketAdapter();
        using var serverCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var server = RunEchoServer(adapter, serverCts.Token);

        // the handshake: SYN, the stack's SYN-ACK, then the host's ACK
        const uint hostSeq = 1000;
        using var syn = TcpTestHarness.CreateTcpPacket(HostIp, HostPort, ServerIp, ServerPort, syn: true, seq: hostSeq);
        adapter.SendFromHost(syn);

        var synAck = await ReceiveSynAck(adapter, TestContext.CancellationToken);
        Assert.AreEqual(ServerIp, synAck.From);
        using var ack = TcpTestHarness.CreateTcpPacket(HostIp, HostPort, ServerIp, ServerPort, ack: true,
            seq: hostSeq + 1, ackNum: synAck.Seq + 1);
        adapter.SendFromHost(ack);

        // a line from the host, and its echo
        using var data = TcpTestHarness.CreateTcpPacket(HostIp, HostPort, ServerIp, ServerPort, ack: true, psh: true,
            seq: hostSeq + 1, ackNum: synAck.Seq + 1, payload: "hello, stack"u8.ToArray());
        adapter.SendFromHost(data);

        var echo = await ReceivePayload(adapter, TestContext.CancellationToken);
        Assert.AreEqual("hello, stack", echo);

        await serverCts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => server);
    }

    private static async Task<(IPAddress From, uint Seq)> ReceiveSynAck(TestPacketAdapter adapter,
        CancellationToken cancellationToken)
    {
        using var packet = await adapter.ReceiveAtHost(cancellationToken);
        var tcp = packet.ExtractTcp();
        Assert.IsTrue(tcp is { Synchronize: true, Acknowledgment: true }, "The stack's first answer is a SYN-ACK.");
        return (packet.SourceAddress, tcp.SequenceNumber);
    }

    // the first segment that carries data: the stack may send a pure ACK before it
    private static async Task<string> ReceivePayload(TestPacketAdapter adapter, CancellationToken cancellationToken)
    {
        while (true) {
            using var packet = await adapter.ReceiveAtHost(cancellationToken);
            var payload = packet.ExtractTcp().Payload;
            if (payload.Length > 0)
                return Encoding.UTF8.GetString(payload.Span);
        }
    }

    private static async Task RunEchoServer(IVpnAdapter adapter, CancellationToken cancellationToken)
    {
        #region Snippet:TcpStackQuickStart
        using var tcpStack = new LocalTcpStack();

        // Packets the stack produces go out through your adapter, which owns each one from here:
        // it disposes each packet once written.
        tcpStack.OnPacketSend = packet => adapter.SendPacketQueued(packet);

        // Packets your adapter captures go into the stack, which copies what it keeps:
        // the handler still owns each one.
        adapter.PacketReceived += (_, packet) => {
            tcpStack.ProcessIncoming(packet);
            packet.Dispose();
        };

        // Listen before the adapter starts: a SYN for an endpoint with no listener draws a reset.
        using var listener = tcpStack.Listen(new IpEndPointValue(IPAddress.Parse("11.0.0.1"), 8080));

        // Start the adapter before the accept loop, which waits for connections.
        await adapter.Start(new VpnAdapterOptions {
            SessionName = "demo",
            VirtualIpNetworkV4 = IpNetwork.Parse("10.0.0.0/24"),
            IncludeNetworks = [new IpNetwork(IPAddress.Parse("11.0.0.1"), 32)]
        }, cancellationToken);

        // Accept connections to the virtual endpoint, each handled on its own.
        await foreach (var client in listener.AcceptAllAsync(cancellationToken))
            _ = HandleAsync(client, cancellationToken);
        #endregion
    }

    #region Snippet:TcpStackEchoServer
    // Echoes what the connection sends until it closes: the handler owns the client,
    // and disposing it closes the connection.
    private static async Task HandleAsync(LocalTcpClient client, CancellationToken cancellationToken)
    {
        await using var connection = client;
        var buffer = new byte[4096];
        while (true) {
            var read = await connection.Stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                return;

            await connection.Stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
    #endregion
}
