using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Toolkit.Net;

// ReSharper disable once InconsistentNaming
/// <summary>
/// Provides helpers for IP addresses: comparing and converting them, finding this device's own addresses, and
/// well-known DNS servers.
/// </summary>
public static class IPAddressUtil
{
    /// <summary>
    /// Gets Google Public DNS's addresses: <c>8.8.8.8</c>, <c>8.8.4.4</c>, <c>2001:4860:4860::8888</c> and
    /// <c>2001:4860:4860::8844</c>.
    /// </summary>
    // Lazy initialization to avoid running heavy static cctor on iOS NE AOT sandbox.
    public static IReadOnlyList<IPAddress> GoogleDnsServers => field ??= [
        IPAddress.Parse("8.8.8.8"),
        IPAddress.Parse("8.8.4.4"),
        IPAddress.Parse("2001:4860:4860::8888"),
        IPAddress.Parse("2001:4860:4860::8844")
    ];

    /// <summary>
    /// Gets the addresses of Cloudflare's 1.1.1.1 for Families that blocks malware and adult content: <c>1.1.1.3</c>,
    /// <c>1.0.0.3</c>, <c>2606:4700:4700::1113</c> and <c>2606:4700:4700::1003</c>.
    /// </summary>
    public static IReadOnlyList<IPAddress> KidsSafeCloudflareDnsServers => field ??= [
        IPAddress.Parse("1.1.1.3"),
        IPAddress.Parse("1.0.0.3"),
        IPAddress.Parse("2606:4700:4700::1113"),
        IPAddress.Parse("2606:4700:4700::1003")
    ];

    /// <summary>
    /// Gets an IPv4 and an IPv6 address of <see cref="GoogleDnsServers"/> and of
    /// <see cref="KidsSafeCloudflareDnsServers"/>: <c>8.8.8.8</c>, <c>2001:4860:4860::8888</c>, <c>1.1.1.3</c> and
    /// <c>2606:4700:4700::1113</c>.
    /// </summary>
    public static IReadOnlyList<IPAddress> ReliableDnsServers => field ??= [
        GoogleDnsServers.First(x => x.IsV4),
        GoogleDnsServers.First(x => x.IsV6),
        KidsSafeCloudflareDnsServers.First(x => x.IsV4),
        KidsSafeCloudflareDnsServers.First(x => x.IsV6)
    ];

    /// <summary>Generates a random unique local IPv6 address (RFC 4193) that ends in a given value.</summary>
    /// <param name="lastValue">The address's last 16 bits.</param>
    /// <returns>
    /// An address in <c>fd00::/8</c> with a random 40-bit global ID, subnet 0 and <paramref name="lastValue"/> as its
    /// last 16 bits, such as <c>fdb6:f6f2:b3c4::1001</c>.
    /// </returns>
    /// <remarks>The global ID comes from a cryptographic random generator, at each call.</remarks>
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

    /// <summary>
    /// Gets the addresses this device sends from to reach the internet, IPv4 then IPv6, as
    /// <see cref="GetPrimaryIpAddress(AddressFamily)"/> finds them.
    /// </summary>
    /// <returns>A task whose result is the addresses: none, one or two.</returns>
    public static async Task<IReadOnlyList<IPAddress>> GetPrimaryIpAddresses()
    {
        var ret = new List<IPAddress>();

        var ipV4Task = GetPrimaryIpAddress(AddressFamily.InterNetwork);
        var ipV6Task = GetPrimaryIpAddress(AddressFamily.InterNetworkV6);
        await Task.WhenAll(ipV4Task, ipV6Task).Vhc();

        if (ipV4Task.Result != null) ret.Add(ipV4Task.Result);
        if (ipV6Task.Result != null) ret.Add(ipV6Task.Result);

        return [.. ret];
    }

    /// <summary>Determines whether this device reaches the internet over IPv6.</summary>
    /// <returns>
    /// A task whose result is <see langword="true"/> if the device has an IPv6 route and an IPv6 address of
    /// <see cref="ReliableDnsServers"/> answers a ping or a DNS query; otherwise, <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// The servers are pinged one after the other, each with <see cref="Ping"/>'s default timeout of 5 seconds; where
    /// none answers, as on a network that drops ICMPv6 echo requests, each is sent a DNS query, with 2 seconds for its
    /// answer. So the answer can take 14 seconds. It never throws.
    /// </remarks>
    public static async Task<bool> IsIpv6Supported()
    {
        try {
            // filter IPv6 addresses
            var dnsServersV6 = ReliableDnsServers
                .Where(x => x.IsV6)
                .ToArray();

            // check with binding to the address family
            // this will throw exception if IPv6 is not supported
            using var udpClient = new UdpClient(AddressFamily.InterNetworkV6);
            udpClient.Connect(dnsServersV6.First(), 53);

            // it may throw error if IPv6 is not supported before creating task
            using var ping = new Ping();
            var pingTasks = dnsServersV6
                .Select(x => ping.SendPingAsync(x));

            foreach (var pingTask in pingTasks) {
                try {
                    if ((await pingTask.Vhc()).Status == IPStatus.Success)
                        return true;
                }
                catch {
                    //ignore
                }
            }

            // A network may drop the echo and still carry IPv6: the servers are resolvers, so one that answers a
            // query over IPv6 tells as much.
            foreach (var dnsServer in dnsServersV6)
                if (await IsDnsServerAnswering(dnsServer).Vhc())
                    return true;
        }
        catch {
            // ignore
        }

        return false;
    }

    private static async Task<bool> IsDnsServerAnswering(IPAddress dnsServer)
    {
        try {
            using var udpClient = new UdpClient(dnsServer.AddressFamily);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var query = Utils.DnsUtils.BuildDnsQuery((ushort)Random.Shared.Next(ushort.MaxValue), "one.one.one.one");
            await udpClient.SendAsync(query, new IPEndPoint(dnsServer, 53), timeoutCts.Token).Vhc();
            await udpClient.ReceiveAsync(timeoutCts.Token).Vhc();
            return true; // any answer came back over IPv6
        }
        catch {
            return false;
        }
    }

    /// <summary>
    /// Gets this device's public addresses, IPv4 then IPv6, as <see cref="GetPublicIpAddress"/> finds them, with no
    /// app name.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the addresses found: none, one or two.</returns>
    /// <remarks>
    /// The two lookups run one after the other; each asks up to two services, each canceled after 5 seconds.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<IReadOnlyList<IPAddress>> GetPublicIpAddresses(CancellationToken cancellationToken)
    {
        var ret = new List<IPAddress>();

        //note: api.ipify.org may not work in parallel call
        var ipV4Task = await GetPublicIpAddress(AddressFamily.InterNetwork, userAgent: null, cancellationToken).Vhc();
        var ipV6Task = await GetPublicIpAddress(AddressFamily.InterNetworkV6, userAgent: null, cancellationToken).Vhc();

        if (ipV4Task != null) ret.Add(ipV4Task);
        if (ipV6Task != null) ret.Add(ipV6Task);

        return [.. ret];
    }

    // What a public-IP lookup calls itself where the caller names no app: the engine, whose name an
    // app built on it carries in its assemblies anyway.
    private const string DefaultUserAgent = "VpnHood";

    /// <summary>Gets this device's public address of a family, as web services see it.</summary>
    /// <param name="addressFamily">
    /// The address family: <see cref="AddressFamily.InterNetwork"/> or <see cref="AddressFamily.InterNetworkV6"/>.
    /// </param>
    /// <param name="userAgent">
    /// The name the asking app gives the services in the User-Agent header, such as its package title, sent as it is;
    /// <see langword="null"/> sends <c>VpnHood</c>.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result is the address, or <see langword="null"/> when no service answered with one.
    /// </returns>
    /// <remarks>
    /// It asks Cloudflare's trace page (<c>www.cloudflare.com/cdn-cgi/trace</c>) over a connection of the family, then,
    /// if that fails or answers with no address, my-ip.io's API for the family, each canceled after 5 seconds. A
    /// failure of both gives <see langword="null"/>.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<IPAddress?> GetPublicIpAddress(AddressFamily addressFamily, string? userAgent,
        CancellationToken cancellationToken)
    {
        try {
            // create linked cancellation token of max 5 seconds
            using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedToken.CancelAfter(TimeSpan.FromSeconds(5));

            // an answer without an address is no answer: the next service is asked
            var ipAddress = await GetPublicIpAddressByCloudflare(addressFamily, userAgent, linkedToken.Token).Vhc();
            if (ipAddress != null)
                return ipAddress;
        }
        // the caller's cancellation is no failed lookup
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch {
            /* continue next service */
        }

        try {
            using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedToken.CancelAfter(TimeSpan.FromSeconds(5));
            return await GetPublicIpAddressByMyIp(addressFamily, userAgent, linkedToken.Token).Vhc();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch {
            /* ignore */
        }

        return null;
    }

    private static async Task<IPAddress?> GetPublicIpAddressByCloudflare(AddressFamily addressFamily,
        string? userAgent, CancellationToken cancellationToken)
    {
        var url = "https://www.cloudflare.com/cdn-cgi/trace";

        var handler = new SocketsHttpHandler {
            ConnectCallback = (context, token) =>
                AddressFamilyConnector.Connect(context.DnsEndPoint, addressFamily, token)
        };

        using var httpClient = CreateHttpClient(handler, userAgent);
        var content = await httpClient
            .GetStringAsync(url, cancellationToken)
            .Vhc();

        // Split the response into lines
        var lines = content.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var ipLine = lines.SingleOrDefault(x => x.StartsWith("ip=", StringComparison.OrdinalIgnoreCase));
        return ipLine != null ? IPAddress.Parse(ipLine.Split('=')[1]) : null;
    }

    private static async Task<IPAddress?> GetPublicIpAddressByMyIp(AddressFamily addressFamily,
        string? userAgent, CancellationToken cancellationToken)
    {
        //var url = addressFamily == AddressFamily.InterNetwork
        //    ? "https://api.ipify.org?format=json"
        //    : "https://api6.ipify.org?format=json";

        var url = addressFamily == AddressFamily.InterNetwork
            ? "https://api4.my-ip.io/v2/ip.json"
            : "https://api6.my-ip.io/v2/ip.json";

        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        using var httpClient = CreateHttpClient(handler, userAgent);
        var json = await httpClient
            .GetStringAsync(url, cancellationToken)
            .Vhc();

        using var document = JsonDocument.Parse(json);
        var ipString = document.RootElement.GetProperty("ip").GetString();
        var ipAddress = IPAddress.Parse(ipString ?? throw new InvalidOperationException());
        return ipAddress.AddressFamily == addressFamily ? ipAddress : null;
    }

    // Unvalidated: a package title is the caller's to choose, and not every one is an HTTP token
    private static HttpClient CreateHttpClient(HttpMessageHandler handler, string? userAgent)
    {
        var httpClient = new HttpClient(handler);
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent ?? DefaultUserAgent);
        return httpClient;
    }

    /// <summary>
    /// Gets the address this device sends from to reach the internet over a family: on most devices, its own address on
    /// the default route.
    /// </summary>
    /// <param name="addressFamily">The address family.</param>
    /// <returns>
    /// A task whose result is the address, or <see langword="null"/> when the device has no route of the family or the
    /// family is not supported.
    /// </returns>
    /// <remarks>
    /// It connects a UDP socket to an address of <see cref="KidsSafeCloudflareDnsServers"/> and reads the local address
    /// the OS chose for it; no packet is sent. The address is private only on a private network: a device with a public address
    /// gets that. The task is complete when returned, and the method never throws.
    /// </remarks>
    public static Task<IPAddress?> GetPrimaryIpAddress(AddressFamily addressFamily)
    {
        try {
            using var udpClient = new UdpClient(addressFamily); // it may throw exception
            return GetPrimaryIpAddress(udpClient);
        }
        catch {
            return Task.FromResult<IPAddress?>(null);
        }
    }

    /// <summary>
    /// Gets the address a UDP client's socket sends from to reach the internet: on most devices, its own address on the
    /// default route of the client's family.
    /// </summary>
    /// <param name="udpClient">
    /// The client. It is connected to an address of <see cref="KidsSafeCloudflareDnsServers"/>, port 53, and stays so:
    /// pass one made for this.
    /// </param>
    /// <returns>
    /// A task whose result is the address, or <see langword="null"/> when there is no route or the client cannot
    /// connect.
    /// </returns>
    /// <remarks>No packet is sent. The task is complete when returned, and the method never throws.</remarks>
    public static Task<IPAddress?> GetPrimaryIpAddress(UdpClient udpClient)
    {
        try {
            var addressFamily = udpClient.Client.AddressFamily;
            var remoteIp = KidsSafeCloudflareDnsServers.First(x => x.AddressFamily == addressFamily);
            udpClient.Connect(remoteIp, 53);
            var endPoint = (IPEndPoint?)udpClient.Client.LocalEndPoint
                           ?? throw new InvalidOperationException("Could not get local endpoint from UdpClient!");

            var ipAddress = endPoint.Address;
            return Task.FromResult(ipAddress.AddressFamily == addressFamily ? ipAddress : null);
        }
        catch {
            return Task.FromResult<IPAddress?>(null);
        }
    }

    /// <summary>
    /// Gets the names that mark the hypervisor and container adapters no other device can reach: <c>Hyper-V
    /// Virtual</c>, <c>vEthernet</c>, <c>VirtualBox</c>, <c>VMware</c>, <c>Docker</c> and <c>WSL</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="GetLanAddresses"/> leaves out an adapter whose name or description contains one, ignoring case, and
    /// that has no gateway: one with a gateway is reachable all the same, as when a Hyper-V external switch moves the
    /// PC's own LAN address onto its <c>vEthernet</c> adapter. They are matched by name because the OS reports most
    /// of them as plain Ethernet; a VPN's tunnel needs no marker, being known by its type.
    /// </para>
    /// <para>
    /// <c>Hyper-V Virtual</c> matches the host's side alone: a guest's own card is a <c>Microsoft Hyper-V Network
    /// Adapter</c>, on its LAN.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> VirtualAdapterMarkers { get; } =
        ["Hyper-V Virtual", "vEthernet", "VirtualBox", "VMware", "Docker", "WSL"];

    /// <summary>
    /// Gets this device's addresses of a family that another device on the local network could dial, best guess
    /// first.
    /// </summary>
    /// <param name="addressFamily">
    /// The address family: <see cref="AddressFamily.InterNetwork"/> or <see cref="AddressFamily.InterNetworkV6"/>.
    /// </param>
    /// <param name="excludeAdapterMarkers">
    /// The names that mark an adapter no other device can reach, found in its name or description, ignoring case;
    /// <see langword="null"/> for <see cref="VirtualAdapterMarkers"/>. A marked adapter with a gateway still counts.
    /// </param>
    /// <returns>A task whose result is the addresses: on a phone or TV usually one, on a PC a few.</returns>
    /// <exception cref="NotSupportedException">
    /// <paramref name="addressFamily"/> is neither IPv4 nor IPv6.
    /// </exception>
    /// <exception cref="NetworkInformationException">The OS could not list the network interfaces.</exception>
    /// <remarks>
    /// <para>
    /// Which network the other device is on cannot be known from here, so the order is a ranking, not proof: the
    /// address on the default route first, as the network with the internet is the one others share, then those of
    /// Wi-Fi and Ethernet adapters, then the rest. The default route's address leads only when an adapter below lists
    /// it, as a connected VPN holds the default route on Windows.
    /// </para>
    /// <para>
    /// Left out: adapters that are down, loopback interfaces, VPN tunnels, mobile broadband (a carrier's address that
    /// nothing on the local network reaches), marked adapters with no gateway, and every loopback and link-local
    /// address, such as the 10.255.255.254 WSL puts on its loopback. An adapter whose state the OS hides counts.
    /// </para>
    /// </remarks>
    public static async Task<IReadOnlyList<IPAddress>> GetLanAddresses(AddressFamily addressFamily,
        IEnumerable<string>? excludeAdapterMarkers = null)
    {
        Verify(addressFamily);
        IReadOnlyList<string> markers = excludeAdapterMarkers?.ToArray() ?? VirtualAdapterMarkers;
        var linkLocalNetwork = addressFamily == AddressFamily.InterNetworkV6
            ? IpNetwork.LinkLocalNetworkV6
            : IpNetwork.LinkLocalNetworkV4;

        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => IsLanInterface(x, markers))
            .OrderBy(x => x.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet ? 0 : 1)
            .SelectMany(x => x.GetIPProperties().UnicastAddresses)
            .Select(x => x.Address)
            .Where(x => x.AddressFamily == addressFamily && !IPAddress.IsLoopback(x) &&
                        !linkLocalNetwork.Contains(x)) // self-assigned with no configuration; reaches nobody
            .Distinct()
            .ToList();

        var defaultRouteAddress = await GetPrimaryIpAddress(addressFamily).Vhc();
        if (defaultRouteAddress != null && addresses.Remove(defaultRouteAddress))
            addresses.Insert(0, defaultRouteAddress);

        return addresses;
    }

    // An interface whose addresses another device on the local network may dial (GetLanAddresses)
    internal static bool IsLanInterface(NetworkInterface networkInterface, IReadOnlyList<string> markers)
    {
        return networkInterface.OperationalStatus is not OperationalStatus.Down &&
               networkInterface.NetworkInterfaceType is not NetworkInterfaceType.Loopback &&
               !IsVirtualAdapter(networkInterface, markers) && !IsMobileBroadbandAdapter(networkInterface);
    }

    // IF_TYPE_PROP_VIRTUAL, which .NET passes on from Windows as it is, with no name of its own
    private const NetworkInterfaceType WindowsProprietaryVirtualType = (NetworkInterfaceType)53;

    // A VPN's tunnel is known by what the OS says it is, never by the name its app gave it: on Windows
    // a proprietary virtual adapter (WinTun, WireGuard-NT, TAP), on Linux a tun or tap device. Android
    // reports most types as Unknown, and its VPN's tunnel is always the kernel's tun<n>, so the name
    // covers it there, as utun<n> on Apple's systems.
    private static bool IsVirtualAdapter(NetworkInterface networkInterface, IReadOnlyList<string> markers)
    {
        return networkInterface.NetworkInterfaceType is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp or
                   WindowsProprietaryVirtualType ||
               IsLinuxTunDevice(networkInterface.Name) ||
               networkInterface.Name.StartsWith("tun", StringComparison.OrdinalIgnoreCase) ||
               networkInterface.Name.StartsWith("utun", StringComparison.OrdinalIgnoreCase) ||
               IsHostOnlyAdapter(networkInterface, markers);
    }

    // A hypervisor's or container's adapter (the markers) that only the host and its guests reach. A
    // Hyper-V external switch moves the PC's own LAN address onto its "vEthernet (<switch>)" adapter,
    // the router as its gateway; the others - internal switches, WSL, Docker, VirtualBox, VMware -
    // route for their guests and have no gateway of their own.
    private static bool IsHostOnlyAdapter(NetworkInterface networkInterface, IReadOnlyList<string> markers)
    {
        return markers.Any(marker =>
                   networkInterface.Name.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                   networkInterface.Description.Contains(marker, StringComparison.OrdinalIgnoreCase)) &&
               networkInterface.GetIPProperties().GatewayAddresses.Count == 0;
    }

    // Every tun and tap device has tun_flags, whatever its name. Linux alone: Android's sandbox may
    // refuse /sys, and the name settles it there.
    private static bool IsLinuxTunDevice(string interfaceName)
    {
        return OperatingSystem.IsLinux() &&
               File.Exists(Path.Combine("/sys/class/net", interfaceName, "tun_flags"));
    }

    // Mobile broadband: a carrier's address, which nothing on the local network can reach. Windows
    // types it; Linux names it ww<...>, the kernel's wwan<n> and systemd's predictable names alike.
    // Phones type none, so their names settle it: Apple's cellular interfaces are pdp_ip<n>, Android's
    // rmnet<...> on Qualcomm's modems and ccmni<n> on MediaTek's. A listener there also dies with the
    // carrier's address whenever the phone lets cellular data idle.
    private static bool IsMobileBroadbandAdapter(NetworkInterface networkInterface)
    {
        var name = networkInterface.Name;
        return networkInterface.NetworkInterfaceType is NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 ||
               (OperatingSystem.IsLinux() && name.StartsWith("ww", StringComparison.Ordinal)) ||
               name.StartsWith("pdp_ip", StringComparison.Ordinal) ||
               name.StartsWith("rmnet", StringComparison.Ordinal) ||
               name.StartsWith("ccmni", StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets the address that stands for any address of a family: <c>0.0.0.0</c> for IPv4, <c>::</c> for IPv6.
    /// </summary>
    /// <param name="addressFamily">The address family.</param>
    /// <returns>The address.</returns>
    /// <exception cref="NotSupportedException">
    /// <paramref name="addressFamily"/> is neither IPv4 nor IPv6.
    /// </exception>
    public static IPAddress GetAnyIpAddress(AddressFamily addressFamily)
    {
        return addressFamily switch {
            AddressFamily.InterNetwork => IPAddress.Any,
            AddressFamily.InterNetworkV6 => IPAddress.IPv6Any,
            _ => throw new NotSupportedException($"{addressFamily} is not supported!")
        };
    }

    /// <summary>Checks that a family is IPv4 or IPv6.</summary>
    /// <param name="addressFamily">The address family.</param>
    /// <exception cref="NotSupportedException">
    /// <paramref name="addressFamily"/> is neither IPv4 nor IPv6.
    /// </exception>
    public static void Verify(AddressFamily addressFamily)
    {
        var isSupported = addressFamily is AddressFamily.InterNetworkV6 or AddressFamily.InterNetwork;
        if (!isSupported)
            throw new NotSupportedException($"{addressFamily} is not supported!");
    }

    /// <summary>Checks that an address's family is IPv4 or IPv6.</summary>
    /// <param name="ipAddress">The address.</param>
    /// <remarks>Every <see cref="IPAddress"/> is one or the other, so the check passes for any address.</remarks>
    public static void Verify(IPAddress ipAddress)
    {
        Verify(ipAddress.AddressFamily);
    }

    /// <summary>
    /// Compares two addresses: an IPv4-mapped IPv6 address as its IPv4 address, every IPv4 address before every IPv6
    /// address, then by number, with no regard to the scope id.
    /// </summary>
    /// <param name="ipAddress1">The first address, or <see langword="null"/>.</param>
    /// <param name="ipAddress2">The second address, or <see langword="null"/>.</param>
    /// <returns>
    /// A negative number if <paramref name="ipAddress1"/> comes first, zero if the two compare as the same address, and
    /// a positive number if it comes after. A <see langword="null"/> address comes first.
    /// </returns>
    /// <remarks>
    /// So 10.0.0.2 and ::ffff:10.0.0.2 compare as the same address, as do fe80::1 and fe80::1%3, though
    /// <see cref="IPAddress.Equals(object)"/> tells each pair apart.
    /// </remarks>
    public static int Compare(IPAddress? ipAddress1, IPAddress? ipAddress2)
    {
        if (ipAddress1 == null && ipAddress2 == null) return 0;
        if (ipAddress1 == null) return -1; // null is less than any address
        if (ipAddress2 == null) return +1; // any address is greater than null

        if (ipAddress1.IsIPv4MappedToIPv6) ipAddress1 = ipAddress1.MapToIPv4();
        if (ipAddress2.IsIPv4MappedToIPv6) ipAddress2 = ipAddress2.MapToIPv4();

        if (ipAddress1.IsV4 && ipAddress2.IsV6)
            return -1;

        if (ipAddress1.IsV6 && ipAddress2.IsV4)
            return +1;

        var span1 = ipAddress1.GetAddressBytesFast(stackalloc byte[16]);
        var span2 = ipAddress2.GetAddressBytesFast(stackalloc byte[16]);
        return span1.SequenceCompareTo(span2);
    }

    /// <summary>
    /// Compares two addresses as <see cref="Compare(IPAddress, IPAddress)"/> does: an IPv4-mapped IPv6 address as its
    /// IPv4 address, every IPv4 address before every IPv6 address, then by number, with no regard to the scope id.
    /// </summary>
    /// <param name="ipAddress1">The first address.</param>
    /// <param name="ipAddress2">The second address.</param>
    /// <returns>
    /// A negative number if <paramref name="ipAddress1"/> comes first, zero if the two compare as the same address, and
    /// a positive number if it comes after.
    /// </returns>
    /// <remarks>
    /// The default value, which holds no address, comes first, as a <see langword="null"/> address does.
    /// <see cref="IpAddressValue.CompareTo"/> keeps an IPv4-mapped address with the IPv6 addresses instead.
    /// </remarks>
    public static int Compare(IpAddressValue ipAddress1, IpAddressValue ipAddress2)
    {
        var value1 = ipAddress1.IsIPv4MappedToIPv6 ? ipAddress1.MapToIPv4() : ipAddress1;
        var value2 = ipAddress2.IsIPv4MappedToIPv6 ? ipAddress2.MapToIPv4() : ipAddress2;
        return value1.CompareAddress(value2);
    }

    /// <summary>
    /// Converts an IPv4 address to its number, its first byte highest: <c>10.0.0.1</c> is <c>0x0A000001</c>.
    /// </summary>
    /// <param name="ipAddress">The address.</param>
    /// <returns>The number, from 0 to 4,294,967,295.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="ipAddress"/> is not IPv4; an IPv4-mapped IPv6 address is not.
    /// </exception>
    public static long ToLong(IPAddress ipAddress)
    {
        if (!ipAddress.IsV4)
            throw new ArgumentException(
                $"Only {AddressFamily.InterNetwork} family can be converted into long!", nameof(ipAddress));

        var bytes = ipAddress.GetAddressBytesFast(stackalloc byte[16]);
        return ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
    }

    /// <summary>
    /// Converts a number to an IPv4 address, as <see cref="ToLong"/> numbers them: <c>0x0A000001</c> is
    /// <c>10.0.0.1</c>.
    /// </summary>
    /// <param name="ipAddress">The number. Only its low 32 bits are read.</param>
    /// <returns>The address.</returns>
    public static IPAddress FromLong(long ipAddress)
    {
        return new IPAddress((uint)IPAddress.NetworkToHostOrder((int)ipAddress));
    }

    /// <summary>Converts an address to its number, its first byte highest.</summary>
    /// <param name="value">The address.</param>
    /// <returns>
    /// The number: up to 2^32 - 1 for IPv4, up to 2^128 - 1 for IPv6. It does not hold the family, which
    /// <see cref="FromBigInteger"/> takes too.
    /// </returns>
    public static BigInteger ToBigInteger(IPAddress value)
    {
        return new BigInteger(value.GetAddressBytesFast(stackalloc byte[16]), true, true);
    }

    /// <summary>Converts a number to an address of a family, as <see cref="ToBigInteger"/> numbers them.</summary>
    /// <param name="value">
    /// The number: up to 2^32 - 1 for IPv4, up to 2^128 - 1 for IPv6.
    /// </param>
    /// <param name="addressFamily">The address family.</param>
    /// <returns>The address.</returns>
    /// <exception cref="NotSupportedException">
    /// <paramref name="addressFamily"/> is neither IPv4 nor IPv6.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="value"/> is negative, or above the family's last address.
    /// </exception>
    public static IPAddress FromBigInteger(BigInteger value, AddressFamily addressFamily)
    {
        Verify(addressFamily);

        // within the family's range: a number above it wrote nothing, and came back as the family's first address
        Span<byte> bytes = stackalloc byte[addressFamily == AddressFamily.InterNetworkV6 ? 16 : 4];
        if (value.Sign < 0 || !value.TryWriteBytes(bytes, out _, true))
            throw new ArgumentOutOfRangeException(nameof(value), value,
                $"The number is no address of {addressFamily}.");

        bytes.Reverse();
        return new IPAddress(bytes);
    }

    // An address's scope id is no part of a network or a range. A copy, and only where there is one to leave out.
    internal static IPAddress WithoutScopeId(IPAddress ipAddress)
    {
        return ipAddress.AddressFamily == AddressFamily.InterNetworkV6 && ipAddress.ScopeId != 0
            ? new IPAddress(ipAddress.GetAddressBytesFast(stackalloc byte[16]))
            : ipAddress;
    }

    /// <summary>Gets the address after an address.</summary>
    /// <param name="ipAddress">The address.</param>
    /// <returns>
    /// The next address, without the scope id; <paramref name="ipAddress"/> itself when it is the last of its family.
    /// </returns>
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

    /// <summary>Gets the address before an address.</summary>
    /// <param name="ipAddress">The address.</param>
    /// <returns>
    /// The previous address, without the scope id; <paramref name="ipAddress"/> itself when it is the first of its
    /// family.
    /// </returns>
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

    /// <summary>Gets the last IPv6 address, <c>ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff</c>.</summary>
    public static IPAddress MaxIPv6Value => field ??= IPAddress.Parse("FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF");

    /// <summary>Gets the first IPv6 address, <c>::</c>.</summary>
    public static IPAddress MinIPv6Value => field ??= IPAddress.Parse("::");

    /// <summary>Gets the last IPv4 address, <c>255.255.255.255</c>.</summary>
    public static IPAddress MaxIPv4Value => field ??= IPAddress.Parse("255.255.255.255");

    /// <summary>Gets the first IPv4 address, <c>0.0.0.0</c>.</summary>
    public static IPAddress MinIPv4Value => field ??= IPAddress.Parse("0.0.0.0");

    /// <summary>
    /// Determines whether an address is the last of its family, <see cref="MaxIPv4Value"/> or
    /// <see cref="MaxIPv6Value"/>.
    /// </summary>
    /// <param name="ipAddress">The address.</param>
    /// <returns><see langword="true"/> if it is; otherwise, <see langword="false"/>.</returns>
    /// <remarks>An IPv6 address's scope id is left out of it.</remarks>
    public static bool IsMaxValue(IPAddress ipAddress)
    {
        Verify(ipAddress);

        // by its bytes, as the order goes: an IPv6 address with a scope id equals none without one
        return ipAddress.GetAddressBytesFast(stackalloc byte[16]).IndexOfAnyExcept(byte.MaxValue) < 0;
    }

    /// <summary>
    /// Determines whether an address is the first of its family, <see cref="MinIPv4Value"/> or
    /// <see cref="MinIPv6Value"/>.
    /// </summary>
    /// <param name="ipAddress">The address.</param>
    /// <returns><see langword="true"/> if it is; otherwise, <see langword="false"/>.</returns>
    /// <remarks>An IPv6 address's scope id is left out of it.</remarks>
    public static bool IsMinValue(IPAddress ipAddress)
    {
        Verify(ipAddress);
        return ipAddress.GetAddressBytesFast(stackalloc byte[16]).IndexOfAnyExcept(byte.MinValue) < 0;
    }

    /// <summary>Hides the host part of an address, for logs and reports.</summary>
    /// <param name="ipAddress">The address.</param>
    /// <returns>
    /// The address's /24 network for IPv4, such as <c>1.2.3.0</c>, or its /48 for IPv6, such as <c>2001:db8:1::</c>,
    /// without the scope id.
    /// </returns>
    /// <remarks>An IPv4-mapped IPv6 address counts as IPv6, so nothing of its IPv4 address is left.</remarks>
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

    /// <summary>
    /// Returns the lower of two addresses, in <see cref="Compare(IPAddress, IPAddress)"/>'s order.
    /// </summary>
    /// <param name="ipAddress1">The first address.</param>
    /// <param name="ipAddress2">The second address.</param>
    /// <returns>The lower address; <paramref name="ipAddress2"/> when the two compare as the same.</returns>
    public static IPAddress Min(IPAddress ipAddress1, IPAddress ipAddress2)
    {
        return Compare(ipAddress1, ipAddress2) < 0 ? ipAddress1 : ipAddress2;
    }

    /// <summary>
    /// Returns the higher of two addresses, in <see cref="Compare(IPAddress, IPAddress)"/>'s order.
    /// </summary>
    /// <param name="ipAddress1">The first address.</param>
    /// <param name="ipAddress2">The second address.</param>
    /// <returns>The higher address; <paramref name="ipAddress2"/> when the two compare as the same.</returns>
    public static IPAddress Max(IPAddress ipAddress1, IPAddress ipAddress2)
    {
        return Compare(ipAddress1, ipAddress2) > 0 ? ipAddress1 : ipAddress2;
    }

    /// <summary>Gets the key under which a server counts a client's requests, to limit a flood from one source.</summary>
    /// <param name="ip">The client's address.</param>
    /// <returns>
    /// An IPv4 address's text, or an IPv6 address's /64 network, such as <c>2001:db8:1:2::/64</c>, as one IPv6
    /// subscriber is given a whole /64.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="ip"/> is <see langword="null"/>.</exception>
    /// <remarks>An IPv4-mapped IPv6 address counts as the IPv4 address it carries.</remarks>
    public static string GetDosKey(IPAddress ip)
    {
        if (ip == null)
            throw new ArgumentNullException(nameof(ip));

        // an IPv4-mapped address is its IPv4 client: left as IPv6, every one of them shared the key ::/64
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        switch (ip.AddressFamily) {
            // IPv4: use full address
            case AddressFamily.InterNetwork:
                return ip.ToString();

            // IPv6: normalize to /64
            case AddressFamily.InterNetworkV6: {
                var bytes = ip.GetAddressBytes();

                // zero last 64 bits
                for (var i = 8; i < 16; i++)
                    bytes[i] = 0;

                var prefix = new IPAddress(bytes);
                return $"{prefix}/64";
            }
            default:
                throw new NotSupportedException("Unsupported IP address family.");
        }
    }
}