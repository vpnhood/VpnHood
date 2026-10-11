using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using VpnHood.Net.Toolkit.Net;

// ReSharper disable StringLiteralTypo
// ReSharper disable InconsistentNaming
// ReSharper disable IdentifierTypo

namespace VpnHood.Net.VpnAdapters.WinTun.WinNative;

public static class Win32IpHelper
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_IPFORWARD_ROW2
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public IP_ADDRESS_PREFIX DestinationPrefix;
        public SOCKADDR_INET NextHop;
        public uint SitePrefixLength;
        public uint ValidLifetime;
        public uint PreferredLifetime;
        public byte OnLinkPrefixLength;
        public uint Metric;
        public uint Protocol;

        [MarshalAs(UnmanagedType.U1)]
        public bool Loopback;

        [MarshalAs(UnmanagedType.U1)]
        public bool AutoconfigureAddress;

        [MarshalAs(UnmanagedType.U1)]
        public bool Publish;

        [MarshalAs(UnmanagedType.U1)]
        public bool Immortal;

        public uint Age;
        public uint Origin;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 32)]
    public struct IP_ADDRESS_PREFIX
    {
        public SOCKADDR_INET Prefix;
        public byte PrefixLength;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct SOCKADDR_INET
    {
        [FieldOffset(0)]
        public SOCKADDR_IN Ipv4;

        [FieldOffset(0)]
        public SOCKADDR_IN6 Ipv6;

        [FieldOffset(0)]
        public ushort si_family;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct SOCKADDR_IN
    {
        public ushort sin_family;
        public ushort sin_port;
        public uint sin_addr;
        public ulong sin_zero;
    }

    [StructLayout(LayoutKind.Sequential, Size = 16)]
    public struct IN6_ADDR
    {
        public ulong lower;
        public ulong upper;
    }


    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct SOCKADDR_IN6
    {
        /// <summary>The address family for the transport address. This member should always be set to AF_INET6.</summary>
        public ushort sin6_family;

        /// <summary>A transport protocol port number.</summary>
        public ushort sin6_port;

        /// <summary>The IPv6 flow information.</summary>
        public uint sin6_flowinfo;

        /// <summary>An IN6_ADDR structure that contains an IPv6 transport address.</summary>
        public IN6_ADDR sin6_addr;

        /// <summary>A ULONG representation of the IPv6 scope identifier that is defined in the <c>sin6_scope_struct</c> member.</summary>
        public uint sin6_scope_id;
    }

    // BOOLEAN members are bytes, so a row read from Windows goes back exactly as read
    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_IPINTERFACE_ROW
    {
        public ushort Family;
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public uint MaxReassemblySize;
        public ulong InterfaceIdentifier;
        public uint MinRouterAdvertisementInterval;
        public uint MaxRouterAdvertisementInterval;
        public byte AdvertisingEnabled;
        public byte ForwardingEnabled;
        public byte WeakHostSend;
        public byte WeakHostReceive;
        public byte UseAutomaticMetric;
        public byte UseNeighborUnreachabilityDetection;
        public byte ManagedAddressConfigurationSupported;
        public byte OtherStatefulConfigurationSupported;
        public byte AdvertiseDefaultRoute;
        public int RouterDiscoveryBehavior;
        public uint DadTransmits;
        public uint BaseReachableTime;
        public uint RetransmitTime;
        public uint PathMtuDiscoveryTimeout;
        public int LinkLocalAddressBehavior;
        public uint LinkLocalAddressTimeout;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] // ScopeLevelCount
        public uint[] ZoneIndices;

        public uint SitePrefixLength;
        public uint Metric;
        public uint NlMtu;
        public byte Connected;
        public byte SupportsWakeUpPatterns;
        public byte SupportsNeighborDiscovery;
        public byte SupportsRouterDiscovery;
        public uint ReachableTime;
        public byte TransmitOffload; // NL_INTERFACE_OFFLOAD_ROD: eight one-bit flags
        public byte ReceiveOffload;
        public byte DisableDefaultRoutes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_UNICASTIPADDRESS_ROW
    {
        public SOCKADDR_INET Address;
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public int PrefixOrigin;
        public int SuffixOrigin;
        public uint ValidLifetime;
        public uint PreferredLifetime;
        public byte OnLinkPrefixLength;
        public byte SkipAsSource;
        public int DadState;
        public uint ScopeId;
        public long CreationTimeStamp;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DNS_INTERFACE_SETTINGS
    {
        public uint Version;
        public ulong Flags;
        public string? Domain;
        public string? NameServer;
        public string? SearchList;
        public uint RegistrationEnabled;
        public uint RegisterAdapterName;
        public uint EnableLLMNR;
        public uint QueryAdapterName;
        public string? ProfileNameServer;
    }

    [DllImport("Iphlpapi.dll", SetLastError = true)]
    public static extern int CreateIpForwardEntry2(ref MIB_IPFORWARD_ROW2 pRoute);

    [DllImport("Iphlpapi.dll")]
    public static extern void InitializeIpInterfaceEntry(out MIB_IPINTERFACE_ROW row);

    [DllImport("Iphlpapi.dll")]
    public static extern int GetIpInterfaceEntry(ref MIB_IPINTERFACE_ROW row);

    [DllImport("Iphlpapi.dll")]
    public static extern int SetIpInterfaceEntry(ref MIB_IPINTERFACE_ROW row);

    [DllImport("Iphlpapi.dll")]
    public static extern void InitializeUnicastIpAddressEntry(out MIB_UNICASTIPADDRESS_ROW row);

    [DllImport("Iphlpapi.dll")]
    public static extern int CreateUnicastIpAddressEntry(ref MIB_UNICASTIPADDRESS_ROW row);

    [DllImport("Iphlpapi.dll")]
    public static extern int DeleteUnicastIpAddressEntry(ref MIB_UNICASTIPADDRESS_ROW row);

    [DllImport("Iphlpapi.dll")]
    public static extern int ConvertInterfaceIndexToLuid(uint interfaceIndex, out ulong interfaceLuid);

    [DllImport("Iphlpapi.dll")]
    public static extern int ConvertInterfaceLuidToGuid(ref ulong interfaceLuid, out Guid interfaceGuid);

    // Windows 10 2004 (build 19041) and later: IsInterfaceDnsSettingsSupported
    [DllImport("Iphlpapi.dll")]
    public static extern int SetInterfaceDnsSettings(Guid interfaceGuid, ref DNS_INTERFACE_SETTINGS settings);


    private static IN6_ADDR ToIN6_ADDR(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 16)
            throw new ArgumentException("Must be an IPv6 address.", nameof(ip));

        return new IN6_ADDR {
            lower = BitConverter.ToUInt64(bytes, 0),
            upper = BitConverter.ToUInt64(bytes, 8)
        };
    }

    private static SOCKADDR_INET ToSOCKADDR_INET(IPAddress ipAddress)
    {
        return ipAddress.AddressFamily == AddressFamily.InterNetwork
            ? new SOCKADDR_INET {
                Ipv4 = new SOCKADDR_IN {
                    sin_family = (ushort)AddressFamily.InterNetwork,
                    sin_addr = BitConverter.ToUInt32(ipAddress.GetAddressBytes(), 0)
                }
            }
            : new SOCKADDR_INET {
                Ipv6 = new SOCKADDR_IN6 {
                    sin6_family = (ushort)AddressFamily.InterNetworkV6,
                    sin6_addr = ToIN6_ADDR(ipAddress)
                }
            };
    }

    public static void AddRoute(uint interfaceIndex, IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        const int noError = 0;
        const int errorObjectAlreadyExists = 5010;

        if (ipNetwork.IsV4) {
            var destinationPrefix = new SOCKADDR_INET {
                Ipv4 = new SOCKADDR_IN {
                    sin_addr = BitConverter.ToUInt32(ipNetwork.Prefix.GetAddressBytes(), 0),
                    sin_family = (ushort)AddressFamily.InterNetwork
                },
                si_family = (ushort)AddressFamily.InterNetwork
            };

            var nextHop = new SOCKADDR_INET {
                Ipv4 = new SOCKADDR_IN {
                    sin_addr = 0,
                    sin_family = (ushort)AddressFamily.InterNetwork
                },
                si_family = (ushort)AddressFamily.InterNetwork
            };

            var row = new MIB_IPFORWARD_ROW2 {
                InterfaceIndex = interfaceIndex,
                DestinationPrefix = new IP_ADDRESS_PREFIX {
                    Prefix = destinationPrefix,
                    PrefixLength = (byte)Math.Clamp(ipNetwork.PrefixLength, 0, 32)
                },
                NextHop = nextHop,
                SitePrefixLength = 0,
                ValidLifetime = 0xFFFFFFFF,
                PreferredLifetime = 0xFFFFFFFF,
                Metric = 0,
                Protocol = 3,
                Loopback = false,
                AutoconfigureAddress = false,
                Publish = false,
                Immortal = true
            };

            var result = CreateIpForwardEntry2(ref row);
            if (result != noError && result != errorObjectAlreadyExists)
                throw new Win32Exception(result, $"Failed to add IPv4 route for {ipNetwork}");
        }

        if (ipNetwork.IsV6) {
            var destinationPrefix = new SOCKADDR_INET {
                Ipv6 = new SOCKADDR_IN6 {
                    sin6_addr = ToIN6_ADDR(ipNetwork.Prefix),
                    sin6_family = (ushort)AddressFamily.InterNetworkV6
                },
                si_family = (ushort)AddressFamily.InterNetworkV6
            };

            var nextHop = new SOCKADDR_INET {
                Ipv6 = new SOCKADDR_IN6 {
                    sin6_addr = new IN6_ADDR { lower = 0, upper = 0 },
                    sin6_family = (ushort)AddressFamily.InterNetworkV6
                },
                si_family = (ushort)AddressFamily.InterNetworkV6
            };

            var row = new MIB_IPFORWARD_ROW2 {
                InterfaceIndex = interfaceIndex,
                DestinationPrefix = new IP_ADDRESS_PREFIX {
                    Prefix = destinationPrefix,
                    PrefixLength = (byte)Math.Clamp(ipNetwork.PrefixLength, 0, 128)
                },
                NextHop = nextHop,
                SitePrefixLength = 0,
                ValidLifetime = 0xFFFFFFFF,
                PreferredLifetime = 0xFFFFFFFF,
                Metric = 0,
                Protocol = 3,
                Loopback = false,
                AutoconfigureAddress = false,
                Publish = false,
                Immortal = true
            };

            var result = CreateIpForwardEntry2(ref row);
            if (result != noError && result != errorObjectAlreadyExists)
                throw new Win32Exception(result, $"Failed to add IPv6 route for {ipNetwork}");
        }
    }

    public static void DisableDuplicateAddressDetection(uint interfaceIndex, AddressFamily addressFamily)
    {
        var row = GetIpInterface(interfaceIndex, addressFamily);
        row.DadTransmits = 0;
        SetIpInterface(row, "disable duplicate address detection");
    }

    // as netsh's "set interface metric=": the metric stops being automatic
    public static void SetMetric(uint interfaceIndex, AddressFamily addressFamily, int metric)
    {
        var row = GetIpInterface(interfaceIndex, addressFamily);
        row.UseAutomaticMetric = 0;
        row.Metric = (uint)metric;
        SetIpInterface(row, $"set the metric {metric}");
    }

    public static void SetMtu(uint interfaceIndex, AddressFamily addressFamily, int mtu)
    {
        var row = GetIpInterface(interfaceIndex, addressFamily);
        row.NlMtu = (uint)mtu;
        SetIpInterface(row, $"set the MTU {mtu}");
    }

    // the row goes back whole, as read, but an IPv4 one read with a site prefix length of 64 must carry 0
    // (documented)
    private static MIB_IPINTERFACE_ROW GetIpInterface(uint interfaceIndex, AddressFamily addressFamily)
    {
        const int noError = 0;

        InitializeIpInterfaceEntry(out var row);
        row.Family = (ushort)addressFamily;
        row.InterfaceIndex = interfaceIndex;
        var result = GetIpInterfaceEntry(ref row);
        if (result != noError)
            throw new Win32Exception(result, $"Failed to get the {addressFamily} interface {interfaceIndex}.");

        if (addressFamily == AddressFamily.InterNetwork)
            row.SitePrefixLength = 0;

        return row;
    }

    private static void SetIpInterface(MIB_IPINTERFACE_ROW row, string change)
    {
        const int noError = 0;

        var result = SetIpInterfaceEntry(ref row);
        if (result != noError)
            throw new Win32Exception(result,
                $"Failed to {change} on the {(AddressFamily)row.Family} interface {row.InterfaceIndex}.");
    }

    // a static address as netsh adds one, but it lasts only as long as the adapter
    public static void AddAddress(uint interfaceIndex, IpNetwork ipNetwork)
    {
        const int noError = 0;
        const int errorObjectAlreadyExists = 5010;
        const int ipPrefixOriginManual = 1;
        const int ipSuffixOriginManual = 1;

        InitializeUnicastIpAddressEntry(out var row);
        row.InterfaceIndex = interfaceIndex;
        row.Address = ToSOCKADDR_INET(ipNetwork.Prefix);
        row.OnLinkPrefixLength = (byte)ipNetwork.PrefixLength;
        row.PrefixOrigin = ipPrefixOriginManual;
        row.SuffixOrigin = ipSuffixOriginManual;
        var result = CreateUnicastIpAddressEntry(ref row);
        if (result != noError && result != errorObjectAlreadyExists)
            throw new Win32Exception(result, $"Failed to add the address {ipNetwork}.");
    }

    public static bool IsInterfaceDnsSettingsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

    // as Set-DnsClientServerAddress: the servers replace the interface's own of their IP version, and stay in
    // its settings
    public static void SetDnsServers(uint interfaceIndex, AddressFamily addressFamily, IEnumerable<IPAddress> dnsServers)
    {
        const int noError = 0;
        const uint dnsInterfaceSettingsVersion1 = 1;
        const ulong dnsSettingIpv6 = 0x0001;
        const ulong dnsSettingNameServer = 0x0002;

        var settings = new DNS_INTERFACE_SETTINGS {
            Version = dnsInterfaceSettingsVersion1,
            Flags = addressFamily == AddressFamily.InterNetworkV6
                ? dnsSettingNameServer | dnsSettingIpv6
                : dnsSettingNameServer,
            NameServer = string.Join(",", dnsServers)
        };
        var result = SetInterfaceDnsSettings(GetInterfaceGuid(interfaceIndex), ref settings);
        if (result != noError)
            throw new Win32Exception(result,
                $"Failed to set the {addressFamily} DNS servers {settings.NameServer} on the interface {interfaceIndex}.");
    }

    private static Guid GetInterfaceGuid(uint interfaceIndex)
    {
        const int noError = 0;

        var result = ConvertInterfaceIndexToLuid(interfaceIndex, out var interfaceLuid);
        if (result != noError)
            throw new Win32Exception(result, $"Failed to get the LUID of the interface {interfaceIndex}.");

        result = ConvertInterfaceLuidToGuid(ref interfaceLuid, out var interfaceGuid);
        if (result != noError)
            throw new Win32Exception(result, $"Failed to get the GUID of the interface {interfaceIndex}.");

        return interfaceGuid;
    }

    public static void DeleteAddress(uint interfaceIndex, IPAddress ipAddress)
    {
        const int noError = 0;
        const int errorNotFound = 1168;

        InitializeUnicastIpAddressEntry(out var row);
        row.InterfaceIndex = interfaceIndex;
        row.Address = ToSOCKADDR_INET(ipAddress);
        var result = DeleteUnicastIpAddressEntry(ref row);
        if (result != noError && result != errorNotFound)
            throw new Win32Exception(result, $"Failed to delete the address {ipAddress}.");
    }
}