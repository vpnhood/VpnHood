using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json;
using VpnHood.Common.Utils;

namespace VpnHood.Common.Net;

// ReSharper disable once InconsistentNaming
public static class IPAddressUtil
{
    public static IPAddress[] GoogleDnsServers { get; } =
    [
        IPAddress.Parse("8.8.8.8"),
        IPAddress.Parse("8.4.4.8"),
        IPAddress.Parse("001:4860:4860::8888"),
        IPAddress.Parse("2001:4860:4860::8844")
    ];

    public static IPAddress[] KidsSafeCloudflareDnsServers { get; } =
    [
        IPAddress.Parse("1.1.1.3"),
        IPAddress.Parse("1.0.0.3"),
        IPAddress.Parse("2606:4700:4700::1113"),
        IPAddress.Parse("2606:4700:4700::1003")
    ];

    public static IPAddress[] ReliableDnsServers { get; } =
    [
        GoogleDnsServers.First(x=>x.IsV4()),
        GoogleDnsServers.First(x=>x.IsV6()),
        KidsSafeCloudflareDnsServers.First(x=>x.IsV4()),
        KidsSafeCloudflareDnsServers.First(x=>x.IsV6())
    ];


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
        try
        {
            // it may throw error if IPv6 is not supported before creating task
            var ping = new Ping();
            var pingTasks = ReliableDnsServers
                .Where(x => x.IsV6())
                .Select(x => ping.SendPingAsync(x));

            foreach (var pingTask in pingTasks)
            {
                try
                {
                    if ((await pingTask.VhConfigureAwait()).Status == IPStatus.Success)
                        return true;
                }
                catch
                {
                    //ignore
                }
            }

        }
        catch
        {
            // ignore
        }

        return false;
    }

    public static async Task<IPAddress[]> GetPublicIpAddresses()
    {
        var ret = new List<IPAddress>();

        //note: api.ipify.org may not work in parallel call
        var ipV4Task = await GetPublicIpAddress(AddressFamily.InterNetwork, TimeSpan.FromSeconds(10)).VhConfigureAwait();
        var ipV6Task = await GetPublicIpAddress(AddressFamily.InterNetworkV6, TimeSpan.FromSeconds(4)).VhConfigureAwait();

        if (ipV4Task != null) ret.Add(ipV4Task);
        if (ipV6Task != null) ret.Add(ipV6Task);

        return ret.ToArray();
    }

    public static Task<IPAddress?> GetPrivateIpAddress(AddressFamily addressFamily)
    {
        try
        {
            using var udpClient = new UdpClient(addressFamily); // it may throw exception
            return GetPrivateIpAddress(udpClient);
        }
        catch
        {
            return Task.FromResult<IPAddress?>(null);
        }
    }

    public static Task<IPAddress?> GetPrivateIpAddress(UdpClient udpClient)
    {
        try
        {
            var addressFamily = udpClient.Client.AddressFamily;
            var remoteIp = KidsSafeCloudflareDnsServers.First(x => x.AddressFamily == addressFamily);
            udpClient.Connect(remoteIp, 53);
            var endPoint = (IPEndPoint)udpClient.Client.LocalEndPoint;
            var ipAddress = endPoint.Address;
            return Task.FromResult(ipAddress.AddressFamily == addressFamily ? ipAddress : null);
        }
        catch
        {
            return Task.FromResult<IPAddress?>(null);
        }
    }

    public static async Task<IPAddress?> GetPublicIpAddress(AddressFamily addressFamily, TimeSpan? timeout = null)
    {
        try { return await GetPublicIpAddressByCloudflare(addressFamily, timeout); }
        catch { /* continue next service */ }

        try { return await GetPublicIpAddressByIpify(addressFamily, timeout); }
        catch { /* ignore */ }

        return null;
    }

    private static async Task<IPAddress?> GetPublicIpAddressByCloudflare(AddressFamily addressFamily, TimeSpan? timeout = null)
    {
        var url = addressFamily == AddressFamily.InterNetwork
            ? "https://1.1.1.1/cdn-cgi/trace"
            : "https://[2606:4700:4700::1111]/cdn-cgi/trace";

        using var httpClient = new HttpClient();
        httpClient.Timeout = timeout ?? TimeSpan.FromSeconds(5);
        var content = await httpClient.GetStringAsync(url).VhConfigureAwait();

        // Split the response into lines
        var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var ipLine = lines.SingleOrDefault(x => x.StartsWith("ip=", StringComparison.OrdinalIgnoreCase));
        return ipLine != null ? IPAddress.Parse(ipLine.Split('=')[1]) : null;
    }

    public static async Task<string?> GetCountryCodeByCloudflare(TimeSpan? timeout = default, CancellationToken cancellationToken = default)
    {
        const string url = "https://cloudflare.com/cdn-cgi/trace";

        using var httpClient = new HttpClient();
        httpClient.Timeout = timeout ?? TimeSpan.FromSeconds(5);
        var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        // Split the response into lines
        var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var ipLine = lines.SingleOrDefault(x => x.StartsWith("loc=", StringComparison.OrdinalIgnoreCase));
        return ipLine?.Split('=')[1];
    }

    private static async Task<IPAddress?> GetPublicIpAddressByIpify(AddressFamily addressFamily, TimeSpan? timeout = null)
    {
        //var url = addressFamily == AddressFamily.InterNetwork
        //    ? "https://api.ipify.org?format=json"
        //    : "https://api6.ipify.org?format=json";

        var url = addressFamily == AddressFamily.InterNetwork
            ? "https://api4.my-ip.io/v2/ip.json"
            : "https://api6.my-ip.io/v2/ip.json";

        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        using var httpClient = new HttpClient(handler);
        httpClient.Timeout = timeout ?? TimeSpan.FromSeconds(5);
        var json = await httpClient.GetStringAsync(url).VhConfigureAwait();
        var document = JsonDocument.Parse(json);
        var ipString = document.RootElement.GetProperty("ip").GetString();
        var ipAddress = IPAddress.Parse(ipString ?? throw new InvalidOperationException());
        return ipAddress.AddressFamily == addressFamily ? ipAddress : null;
    }

    public static IPAddress GetAnyIpAddress(AddressFamily addressFamily)
    {
        return addressFamily switch
        {
            AddressFamily.InterNetwork => IPAddress.Any,
            AddressFamily.InterNetworkV6 => IPAddress.IPv6Any,
            _ => throw new NotSupportedException($"{addressFamily} is not supported!")
        };
    }

    public static bool IsSupported(AddressFamily addressFamily)
    {
        return addressFamily
            is AddressFamily.InterNetworkV6
            or AddressFamily.InterNetwork;
    }

    public static void Verify(AddressFamily addressFamily)
    {
        if (!IsSupported(addressFamily))
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
        if (ipAddress1.AddressFamily == AddressFamily.InterNetwork &&
            ipAddress2.AddressFamily == AddressFamily.InterNetworkV6)
            return -1;

        if (ipAddress1.AddressFamily == AddressFamily.InterNetworkV6 &&
            ipAddress2.AddressFamily == AddressFamily.InterNetwork)
            return +1;

        var bytes1 = ipAddress1.GetAddressBytes();
        var bytes2 = ipAddress2.GetAddressBytes();

        for (var i = 0; i < bytes1.Length; i++)
        {
            if (bytes1[i] < bytes2[i]) return -1;
            if (bytes1[i] > bytes2[i]) return +1;
        }

        return 0;
    }

    public static long ToLong(IPAddress ipAddress)
    {
        if (ipAddress.AddressFamily != AddressFamily.InterNetwork)
            throw new InvalidOperationException($"Only {AddressFamily.InterNetwork} family can be converted into long!");

        var bytes = ipAddress.GetAddressBytes();
        return ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
    }

    public static IPAddress FromLong(long ipAddress)
    {
        return new IPAddress((uint)IPAddress.NetworkToHostOrder((int)ipAddress));
    }

    public static BigInteger ToBigInteger(IPAddress value)
    {
        return new BigInteger(value.GetAddressBytes(), true, true);
    }

    public static IPAddress FromBigInteger(BigInteger value, AddressFamily addressFamily)
    {
        Verify(addressFamily);

        var bytes = new byte[addressFamily == AddressFamily.InterNetworkV6 ? 16 : 4];
        value.TryWriteBytes(bytes, out _, true);
        Array.Reverse(bytes);
        return new IPAddress(bytes);
    }

    public static IPAddress Increment(IPAddress ipAddress)
    {
        Verify(ipAddress);

        var bytes = ipAddress.GetAddressBytes();

        for (var k = bytes.Length - 1; k >= 0; k--)
        {
            if (bytes[k] == byte.MaxValue)
            {
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

        var bytes = ipAddress.GetAddressBytes();

        for (var k = bytes.Length - 1; k >= 0; k--)
        {
            if (bytes[k] == byte.MinValue)
            {
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
        return ipAddress.AddressFamily switch
        {
            AddressFamily.InterNetworkV6 => ipAddress.Equals(MaxIPv6Value),
            AddressFamily.InterNetwork => ipAddress.Equals(MaxIPv4Value),
            _ => throw new NotSupportedException($"{ipAddress.AddressFamily} is not supported!")
        };
    }

    public static bool IsMinValue(IPAddress ipAddress)
    {
        Verify(ipAddress);
        return ipAddress.AddressFamily switch
        {
            AddressFamily.InterNetwork => ipAddress.Equals(MinIPv4Value),
            AddressFamily.InterNetworkV6 => ipAddress.Equals(MinIPv6Value),
            _ => throw new NotSupportedException($"{ipAddress.AddressFamily} is not supported!")
        };
    }

    public static IPAddress Anonymize(IPAddress ipAddress)
    {
        if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ipAddress.GetAddressBytes();
            bytes[^1] = 0;
            return new IPAddress(bytes);
        }
        else
        {
            var bytes = ipAddress.GetAddressBytes();
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