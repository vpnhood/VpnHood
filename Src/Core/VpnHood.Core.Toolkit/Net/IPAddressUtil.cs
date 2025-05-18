using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Toolkit.Net;

// ReSharper disable once InconsistentNaming
public static class IPAddressUtil
{
    public static IPAddress[] GoogleDnsServers { get; } = [
        IPAddress.Parse("8.8.8.8"),
        IPAddress.Parse("8.8.4.4"),
        IPAddress.Parse("2001:4860:4860::8888"),
        IPAddress.Parse("2001:4860:4860::8844")
    ];

    public static IPAddress[] KidsSafeCloudflareDnsServers { get; } = [
        IPAddress.Parse("1.1.1.3"),
        IPAddress.Parse("1.0.0.3"),
        IPAddress.Parse("2606:4700:4700::1113"),
        IPAddress.Parse("2606:4700:4700::1003")
    ];

    public static IPAddress[] ReliableDnsServers { get; } = [
        GoogleDnsServers.First(x => x.IsV4()),
        GoogleDnsServers.First(x => x.IsV6()),
        KidsSafeCloudflareDnsServers.First(x => x.IsV4()),
        KidsSafeCloudflareDnsServers.First(x => x.IsV6())
    ];

    public static IPAddress GenerateUlaAddress(ushort lastValue)
    {
        var randomBytes = new byte[5]; // 40-bit random part
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);

        // Construct the ULA prefix
        var ulaBytes = new byte[16]; // Full IPv6 address size
        ulaBytes[0] = 0xfd; // ULA always starts with fd00::/8
        ulaBytes[1] = randomBytes[0]; // Next 40 bits (5 bytes) are randomly generated
        ulaBytes[2] = randomBytes[1];
        ulaBytes[3] = randomBytes[2];
        ulaBytes[4] = randomBytes[3];
        ulaBytes[5] = randomBytes[4];

        // Set the last 2 bytes to the provided lastValue (big-endian)
        ulaBytes[14] = (byte)(lastValue >> 8);
        ulaBytes[15] = (byte)(lastValue & 0xFF);

        return new IPAddress(ulaBytes);
    }

    public static async Task<IPAddress[]> GetPrivateIpAddresses()
    {
        var ret = new List<IPAddress>();

        var ipV4Task = GetPrivateIpAddress(AddressFamily.InterNetwork);
        var ipV6Task = GetPrivateIpAddress(AddressFamily.InterNetworkV6);
        await Task.WhenAll(ipV4Task, ipV6Task).VhConfigureAwait();

        if (ipV4Task.Result != null) ret.Add(ipV4Task.Result);
        if (ipV6Task.Result != null) ret.Add(ipV6Task.Result);

        return ret.ToArray();
    }

    public static async Task<bool> IsIpv6Supported()
    {
        try {
            // filter IPv6 addresses
            var dnsServersV6 = ReliableDnsServers
                .Where(x => x.IsV6())
                .ToArray();

            // check with binding to the address family
            // this will throw exception if IPv6 is not supported
            using var udpClient = new UdpClient(AddressFamily.InterNetworkV6);
            udpClient.Connect(dnsServersV6.First(), 53);

            // it may throw error if IPv6 is not supported before creating task
            var ping = new Ping();
            var pingTasks = dnsServersV6
                .Select(x => ping.SendPingAsync(x));

            foreach (var pingTask in pingTasks) {
                try {
                    if ((await pingTask.VhConfigureAwait()).Status == IPStatus.Success)
                        return true;
                }
                catch {
                    //ignore
                }
            }
        }
        catch {
            // ignore
        }

        return false;
    }

    public static async Task<IPAddress[]> GetPublicIpAddresses(CancellationToken cancellationToken)
    {
        var ret = new List<IPAddress>();

        //note: api.ipify.org may not work in parallel call
        var ipV4Task = await GetPublicIpAddress(AddressFamily.InterNetwork, cancellationToken).VhConfigureAwait();
        var ipV6Task = await GetPublicIpAddress(AddressFamily.InterNetworkV6, cancellationToken).VhConfigureAwait();

        if (ipV4Task != null) ret.Add(ipV4Task);
        if (ipV6Task != null) ret.Add(ipV6Task);

        return ret.ToArray();
    }

    public static async Task<IPAddress?> GetPublicIpAddress(AddressFamily addressFamily,
        CancellationToken cancellationToken)
    {
        try {
            // create linked cancellation token of max 5 seconds
            using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedToken.CancelAfter(TimeSpan.FromSeconds(5));
            return await GetPublicIpAddressByCloudflare(addressFamily, linkedToken.Token);
        }
        catch {
            /* continue next service */
        }

        try {
            using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedToken.CancelAfter(TimeSpan.FromSeconds(5));
            return await GetPublicIpAddressByIpify(addressFamily, linkedToken.Token);
        }
        catch {
            /* ignore */
        }

        return null;
    }

    private static async Task<IPAddress?> GetPublicIpAddressByCloudflare(AddressFamily addressFamily,
        CancellationToken cancellationToken)
    {
        var url = addressFamily == AddressFamily.InterNetwork
            ? "https://1.1.1.1/cdn-cgi/trace"
            : "https://[2606:4700:4700::1111]/cdn-cgi/trace";

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "VpnHood");
        var content = await httpClient
            .GetStringAsync(url)
            .VhWait(cancellationToken)
            .VhConfigureAwait();

        // Split the response into lines
        var lines = content.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var ipLine = lines.SingleOrDefault(x => x.StartsWith("ip=", StringComparison.OrdinalIgnoreCase));
        return ipLine != null ? IPAddress.Parse(ipLine.Split('=')[1]) : null;
    }

    private static async Task<IPAddress?> GetPublicIpAddressByIpify(AddressFamily addressFamily,
        CancellationToken cancellationToken)
    {
        //var url = addressFamily == AddressFamily.InterNetwork
        //    ? "https://api.ipify.org?format=json"
        //    : "https://api6.ipify.org?format=json";

        var url = addressFamily == AddressFamily.InterNetwork
            ? "https://api4.my-ip.io/v2/ip.json"
            : "https://api6.my-ip.io/v2/ip.json";

        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        using var httpClient = new HttpClient(handler);
        httpClient.DefaultRequestHeaders.Add("User-Agent", "VpnHood");
        var json = await httpClient
            .GetStringAsync(url)
            .VhWait(cancellationToken)
            .VhConfigureAwait();

        var document = JsonDocument.Parse(json);
        var ipString = document.RootElement.GetProperty("ip").GetString();
        var ipAddress = IPAddress.Parse(ipString ?? throw new InvalidOperationException());
        return ipAddress.AddressFamily == addressFamily ? ipAddress : null;
    }

    public static Task<IPAddress?> GetPrivateIpAddress(AddressFamily addressFamily)
    {
        try {
            using var udpClient = new UdpClient(addressFamily); // it may throw exception
            return GetPrivateIpAddress(udpClient);
        }
        catch {
            return Task.FromResult<IPAddress?>(null);
        }
    }

    public static Task<IPAddress?> GetPrivateIpAddress(UdpClient udpClient)
    {
        try {
            var addressFamily = udpClient.Client.AddressFamily;
            var remoteIp = KidsSafeCloudflareDnsServers.First(x => x.AddressFamily == addressFamily);
            udpClient.Connect(remoteIp, 53);
            var endPoint = (IPEndPoint)udpClient.Client.LocalEndPoint;
            var ipAddress = endPoint.Address;
            return Task.FromResult(ipAddress.AddressFamily == addressFamily ? ipAddress : null);
        }
        catch {
            return Task.FromResult<IPAddress?>(null);
        }
    }

    public static IPAddress GetAnyIpAddress(AddressFamily addressFamily)
    {
        return addressFamily switch {
            AddressFamily.InterNetwork => IPAddress.Any,
            AddressFamily.InterNetworkV6 => IPAddress.IPv6Any,
            _ => throw new NotSupportedException($"{addressFamily} is not supported!")
        };
    }

    public static void Verify(AddressFamily addressFamily)
    {
        var isSupported = addressFamily is AddressFamily.InterNetworkV6 or AddressFamily.InterNetwork;
        if (!isSupported)
            throw new NotSupportedException($"{addressFamily} is not supported!");
    }

    public static void Verify(IPAddress ipAddress)
    {
        Verify(ipAddress.AddressFamily);
    }

    public static int Compare(IPAddress ipAddress1, IPAddress ipAddress2)
    {
        Verify(ipAddress1);
        Verify(ipAddress2);

        // resolve mapped addresses
        if (ipAddress1.IsIPv4MappedToIPv6) ipAddress1 = ipAddress1.MapToIPv4();
        if (ipAddress2.IsIPv4MappedToIPv6) ipAddress2 = ipAddress2.MapToIPv4();

        // compare
        if (ipAddress1.IsV4() && ipAddress2.IsV6())
            return -1;

        if (ipAddress1.IsV6() && ipAddress2.IsV4())
            return +1;

        var span1 = ipAddress1.GetAddressBytesFast(stackalloc byte[16]);
        var span2 = ipAddress2.GetAddressBytesFast(stackalloc byte[16]);
        return span1.SequenceCompareTo(span2);
    }

    public static long ToLong(IPAddress ipAddress)
    {
        if (!ipAddress.IsV4())
            throw new InvalidOperationException(
                $"Only {AddressFamily.InterNetwork} family can be converted into long!");

        var bytes = ipAddress.GetAddressBytesFast(stackalloc byte[16]);
        return ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
    }

    public static IPAddress FromLong(long ipAddress)
    {
        return new IPAddress((uint)IPAddress.NetworkToHostOrder((int)ipAddress));
    }

    public static BigInteger ToBigInteger(IPAddress value)
    {
        return new BigInteger(value.GetAddressBytesFast(stackalloc byte[16]), true, true);
    }

    public static IPAddress FromBigInteger(BigInteger value, AddressFamily addressFamily)
    {
        Verify(addressFamily);

        Span<byte> bytes = stackalloc byte[addressFamily == AddressFamily.InterNetworkV6 ? 16 : 4];
        value.TryWriteBytes(bytes, out _, true);
        bytes.Reverse();
        return new IPAddress(bytes);
    }

    public static IPAddress Increment(IPAddress ipAddress)
    {
        Verify(ipAddress);

        var bytes = ipAddress.GetAddressBytesFast(stackalloc byte[16]);

        for (var k = bytes.Length - 1; k >= 0; k--) {
            if (bytes[k] == byte.MaxValue) {
                bytes[k] = byte.MinValue;
                continue;
            }

            bytes[k]++;

            return new IPAddress(bytes);
        }

        // Un-increment-able, return the original address.
        return ipAddress;
    }

    public static IPAddress Decrement(IPAddress ipAddress)
    {
        Verify(ipAddress);

        var bytes = ipAddress.GetAddressBytesFast(stackalloc byte[16]);

        for (var k = bytes.Length - 1; k >= 0; k--) {
            if (bytes[k] == byte.MinValue) {
                bytes[k] = byte.MaxValue;
                continue;
            }

            bytes[k]--;

            return new IPAddress(bytes);
        }

        // Un-decrement-able, return the original address.
        return ipAddress;
    }

    public static IPAddress MaxIPv6Value { get; } = IPAddress.Parse("FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF");
    public static IPAddress MinIPv6Value { get; } = IPAddress.Parse("::");
    public static IPAddress MaxIPv4Value { get; } = IPAddress.Parse("255.255.255.255");
    public static IPAddress MinIPv4Value { get; } = IPAddress.Parse("0.0.0.0");

    public static bool IsMaxValue(IPAddress ipAddress)
    {
        Verify(ipAddress);
        return ipAddress.AddressFamily switch {
            AddressFamily.InterNetworkV6 => ipAddress.Equals(MaxIPv6Value),
            AddressFamily.InterNetwork => ipAddress.Equals(MaxIPv4Value),
            _ => throw new NotSupportedException($"{ipAddress.AddressFamily} is not supported!")
        };
    }

    public static bool IsMinValue(IPAddress ipAddress)
    {
        Verify(ipAddress);
        return ipAddress.AddressFamily switch {
            AddressFamily.InterNetwork => ipAddress.Equals(MinIPv4Value),
            AddressFamily.InterNetworkV6 => ipAddress.Equals(MinIPv6Value),
            _ => throw new NotSupportedException($"{ipAddress.AddressFamily} is not supported!")
        };
    }

    public static IPAddress Anonymize(IPAddress ipAddress)
    {
        if (ipAddress.AddressFamily == AddressFamily.InterNetwork) {
            var bytes = ipAddress.GetAddressBytesFast(stackalloc byte[16]);
            bytes[^1] = 0;
            return new IPAddress(bytes);
        }
        else {
            var bytes = ipAddress.GetAddressBytesFast(stackalloc byte[16]);
            for (var i = 6; i < bytes.Length; i++)
                bytes[i] = 0;
            return new IPAddress(bytes);
        }
    }

    public static IPAddress Min(IPAddress ipAddress1, IPAddress ipAddress2)
    {
        return Compare(ipAddress1, ipAddress2) < 0 ? ipAddress1 : ipAddress2;
    }

    public static IPAddress Max(IPAddress ipAddress1, IPAddress ipAddress2)
    {
        return Compare(ipAddress1, ipAddress2) > 0 ? ipAddress1 : ipAddress2;
    }
}