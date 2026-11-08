using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Toolkit.Utils;

public static class DnsUtils
{
    private const ushort DnsResponseFlag = 0x8000; // QR
    private const ushort DnsTruncatedFlag = 0x0200; // TC
    private const ushort DnsResponseCodeMask = 0x000F; // RCODE

    // The resolvers of the networks the machine reaches beyond itself by: adapters up with a gateway, no
    // loopback or tunnel. Not Windows' fec0:0:0:ffff:: placeholders, which it lists where IPv6 has none, nor a
    // resolver on this machine: a stub, which asks from a process of its own. Linux's .NET reads /etc/resolv.conf
    // for every adapter, which names only systemd-resolved's stub (127.0.0.53) where it runs, so the upstream list
    // it writes stands in (seen on Ubuntu 26.04, where the kill switch's block stopped the stub's own lookups).
    public static IReadOnlyList<IPAddress> GetNetworkDnsServers()
    {
        var dnsServers = NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => x is {
                OperationalStatus: OperationalStatus.Up,
                NetworkInterfaceType: not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            })
            .Select(x => x.GetIPProperties())
            .Where(x => x.GatewayAddresses.Count > 0)
            .SelectMany(x => x.DnsAddresses)
            .Where(x => !x.IsIPv6SiteLocal && !IPAddress.IsLoopback(x))
            .Distinct()
            .ToArray();

        return dnsServers.Length == 0 && OperatingSystem.IsLinux()
            ? ReadResolvConf(SystemdResolvedUpstreamFilePath)
            : dnsServers;
    }

    private const string SystemdResolvedUpstreamFilePath = "/run/systemd/resolve/resolv.conf";

    // A resolv.conf's nameservers, but loopback ones; none where the file is missing.
    private static IPAddress[] ReadResolvConf(string filePath)
    {
        if (!File.Exists(filePath))
            return [];

        return File.ReadAllLines(filePath)
            .Select(x => x.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(x => x is ["nameserver", _, ..])
            .Select(x => IPAddress.TryParse(x[1], out var address) ? address : null)
            .OfType<IPAddress>()
            .Where(x => !IPAddress.IsLoopback(x))
            .Distinct()
            .ToArray();
    }

    public static async Task<IPHostEntry> GetHostEntry(string host, IPEndPoint dnsEndPoint,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var udpClientTemp = new UdpClient();
        return await GetHostEntry(host, dnsEndPoint, udpClientTemp, timeout, cancellationToken).Vhc();
    }

    public static async Task<IPHostEntry> GetHostEntry(string host, IPEndPoint dnsEndPoint,
        UdpClient udpClient, TimeSpan timeout, CancellationToken cancellationToken)
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
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        await udpClient
            .SendAsync(query.AsMemory(), linkedCts.Token)
            .Vhc();

        // Wait for response with a timeout
        var task = await udpClient
            .ReceiveAsync(linkedCts.Token)
            .Vhc();

        var response = task.Buffer;

        // Parse the DNS response
        var hostEntry = ParseDnsResponse(response, queryId);
        hostEntry.HostName = host;
        return hostEntry;
    }

    public static byte[] BuildDnsQuery(ushort queryId, string host)
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

    public static IPHostEntry ParseDnsResponse(byte[] response, ushort queryId)
    {
        using var stream = new MemoryStream(response);
        using var reader = new BinaryReader(stream);

        // Read Header
        var responseId = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        if (responseId != queryId) {
            throw new InvalidOperationException("Response ID does not match query ID.");
        }

        // a response, whole and without an error: a truncated one may lack some of the name's addresses, which its
        // caller cannot tell, as one asking whether an address is still the name's
        var flags = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        if ((flags & DnsResponseFlag) == 0)
            throw new InvalidOperationException("The DNS message is not a response.");

        if ((flags & DnsTruncatedFlag) != 0)
            throw new InvalidOperationException("The DNS response is truncated.");

        if ((flags & DnsResponseCodeMask) != 0)
            throw new InvalidOperationException(
                $"The DNS response has an error. ResponseCode: {flags & DnsResponseCodeMask}");

        var questionCount = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        var answerCount = (ushort)IPAddress.NetworkToHostOrder(reader.ReadInt16());
        // ReSharper disable UnusedVariable
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

            if (type == 1 && @class == 1 && dataLength == 4) // Type A, Class IN, an IPv4 address
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
            AddressList = [.. ipAddresses]
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