using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace VpnHood.Test.Providers;

// A resolver on the loopback: it answers each query with the addresses of the query's family, A or AAAA, as a whole
// response with no error, and with no record where it has none of that family. It records the families it was asked
// for. An answer delay per family lets a test make one family's answer come late
public sealed class TestDnsResolver : IDisposable
{
    private const ushort RecordTypeA = 1;
    private const ushort RecordTypeAaaa = 28;
    private readonly UdpClient _udpClient;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly CancellationToken _disposeToken;
    private readonly ConcurrentQueue<AddressFamily> _queries = new();
    private readonly IReadOnlyList<IPAddress> _addresses;
    private readonly Task _serveTask;

    // at the endpoint given, or at a port of its own on IPv4's loopback
    public TestDnsResolver(IReadOnlyList<IPAddress> addresses, IPEndPoint? endPoint = null)
    {
        _udpClient = new UdpClient(endPoint ?? new IPEndPoint(IPAddress.Loopback, 0));
        _addresses = addresses;
        _disposeToken = _disposeCts.Token;
        _serveTask = Task.Run(Serve, CancellationToken.None);
    }

    public IPEndPoint EndPoint => _udpClient.Client.LocalEndPoint as IPEndPoint ??
                                 throw new InvalidOperationException("The resolver has no endpoint.");

    // the families of the queries it got, in their order
    public IReadOnlyCollection<AddressFamily> Queries => _queries;

    // how long an answer of a family waits before it goes
    public TimeSpan AnswerDelayIpV4 { get; init; }
    public TimeSpan AnswerDelayIpV6 { get; init; }

    // the answers' error code, as 2 for a server failure, with no record; 0 for none
    public byte ResponseCode { get; init; }

    private async Task Serve()
    {
        try {
            while (true) {
                var request = await _udpClient.ReceiveAsync(_disposeToken);
                _ = Answer(request.Buffer, request.RemoteEndPoint);
            }
        }
        catch (Exception) when (_disposeToken.IsCancellationRequested) {
            // the resolver's end
        }
    }

    private async Task Answer(byte[] query, IPEndPoint remoteEndPoint)
    {
        // the query's type is the low byte of the two before its class
        var addressFamily = query[^3] == RecordTypeAaaa ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        _queries.Enqueue(addressFamily);

        try {
            var delay = addressFamily == AddressFamily.InterNetworkV6 ? AnswerDelayIpV6 : AnswerDelayIpV4;
            await Task.Delay(delay, _disposeToken);
            var addresses = ResponseCode == 0
                ? _addresses.Where(x => x.AddressFamily == addressFamily).ToArray()
                : [];

            await _udpClient.SendAsync(BuildAnswer(query, addresses, ResponseCode), remoteEndPoint, _disposeToken);
        }
        catch (Exception) when (_disposeToken.IsCancellationRequested) {
            // the resolver's end
        }
    }

    // the query as a response with the error code given, and one record per address
    private static byte[] BuildAnswer(byte[] query, IReadOnlyList<IPAddress> addresses, byte responseCode)
    {
        using var stream = new MemoryStream();
        stream.Write(query);
        stream.Position = 2;
        stream.Write([0x81, (byte)(0x80 | responseCode)]); // a response, recursion desired and available
        stream.Position = 6;
        stream.Write([(byte)(addresses.Count >> 8), (byte)addresses.Count]);
        stream.Position = stream.Length;
        foreach (var address in addresses) {
            var recordType = address.AddressFamily == AddressFamily.InterNetworkV6 ? RecordTypeAaaa : RecordTypeA;
            var data = address.GetAddressBytes();
            stream.Write([0xC0, 0x0C, 0, (byte)recordType, 0, 1, 0, 0, 0, 60, 0, (byte)data.Length]);
            stream.Write(data);
        }

        return stream.ToArray();
    }

    public void Dispose()
    {
        _disposeCts.Cancel();
        _udpClient.Dispose();
        _serveTask.Wait(TimeSpan.FromSeconds(5));
        _disposeCts.Dispose();
    }
}
