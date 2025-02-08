using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VpnHood.Core.Common.Utils;

public static class DnsResolver
{
    public static async Task<IPHostEntry> GetHostEntry(string host, IPEndPoint dnsEndPoint,
        int timeout, CancellationToken cancellationToken)
    {
        using var udpClientTemp = new UdpClient();
        return await GetHostEntry(host, dnsEndPoint, udpClientTemp, timeout, cancellationToken);
    }

    public static async Task<IPHostEntry> GetHostEntry(string host, IPEndPoint dnsEndPoint,
        UdpClient udpClient, int timeout, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host)) {
            throw new ArgumentException("Host cannot be null or empty", nameof(host));
        }

        if (dnsEndPoint == null) {
            throw new ArgumentNullException(nameof(dnsEndPoint));
        }

        udpClient.Connect(dnsEndPoint);

        // Build DNS query
        var queryId = (ushort)new Random().Next(ushort.MaxValue);
        var query = BuildDnsQuery(queryId, host);

        // Send the DNS query
        await udpClient
            .SendAsync(query, query.Length)
            .VhWait(timeout, cancellationToken)
            .VhConfigureAwait();

        // Wait for response with a timeout
        var task = await udpClient
            .ReceiveAsync()
            .VhWait(timeout, cancellationToken)
            .VhConfigureAwait();

        var response = task.Buffer;

        // Parse the DNS response
        var hostEntry = ParseDnsResponse(response, queryId);
        hostEntry.HostName = host;
        return hostEntry;
    }

    private static byte[] BuildDnsQuery(ushort queryId, string host)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        // Header
        writer.Write((ushort)IPAddress.HostToNetworkOrder((short)queryId)); // Query ID
        writer.Write((ushort)IPAddress.HostToNetworkOrder((short)0x0100)); // Standard query
        writer.Write((ushort)IPAddress.HostToNetworkOrder((short)1)); // Questions
        writer.Write((ushort)IPAddress.HostToNetworkOrder((short)0)); // Answer RRs
        writer.Write((ushort)IPAddress.HostToNetworkOrder((short)0)); // Authority RRs
        writer.Write((ushort)IPAddress.HostToNetworkOrder((short)0)); // Additional RRs

        // Question section
        foreach (var label in host.Split('.')) {
            writer.Write((byte)label.Length);
            writer.Write(Encoding.ASCII.GetBytes(label));
        }

        writer.Write((byte)0); // End of hostname
        writer.Write((ushort)IPAddress.HostToNetworkOrder((short)1)); // Type A
        writer.Write((ushort)IPAddress.HostToNetworkOrder((short)1)); // Class IN

        return stream.ToArray();
    }

    private static IPHostEntry ParseDnsResponse(byte[] response, ushort queryId)
    {
        using var stream = new MemoryStream(response);
        using var reader = new BinaryReader(stream);

        // Read Header
        var responseId = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        if (responseId != queryId) {
            throw new InvalidOperationException("Response ID does not match query ID.");
        }

        // ReSharper disable UnusedVariable
        var flags = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        var questionCount = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        var answerCount = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        var authorityCount = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        var additionalCount = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        // ReSharper restore UnusedVariable

        // Skip question section
        for (var i = 0; i < questionCount; i++) {
            SkipDomainName(reader);
            reader.ReadUInt16(); // Type
            reader.ReadUInt16(); // Class
        }

        // Read answers
        var ipAddresses = new List<IPAddress>();
        for (var i = 0; i < answerCount; i++) {
            SkipDomainName(reader);
            // ReSharper disable UnusedVariable
            var type = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
            var @class = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
            var ttl = (uint)IPAddress.NetworkToHostOrder(reader.ReadInt32());
            var dataLength = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
            // ReSharper restore UnusedVariable

            if (type == 1 && @class == 1) // Type A, Class IN
            {
                var addressBytes = reader.ReadBytes(dataLength);
                ipAddresses.Add(new IPAddress(addressBytes));
            }
            else {
                reader.ReadBytes(dataLength); // Skip non-A records
            }
        }

        if (ipAddresses.Count == 0) {
            throw new Exception("No valid A records found in DNS response.");
        }

        return new IPHostEntry {
            HostName = "",
            AddressList = ipAddresses.ToArray()
        };
    }

    private static void SkipDomainName(BinaryReader reader)
    {
        while (true) {
            var length = reader.ReadByte();
            if (length == 0) {
                break;
            }

            if ((length & 0xC0) == 0xC0) // Compressed name
            {
                reader.ReadByte();
                break;
            }

            reader.ReadBytes(length);
        }
    }
}