using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using VpnHood.Core.Toolkit.Net;
// ReSharper disable StringLiteralTypo
// ReSharper disable InconsistentNaming
// ReSharper disable IdentifierTypo

namespace VpnHood.Core.VpnAdapters.WinTun.WinNative;

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
        [FieldOffset(0)] public SOCKADDR_IN Ipv4;
        [FieldOffset(0)] public SOCKADDR_IN6 Ipv6;
        [FieldOffset(0)] public ushort si_family;
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

    [DllImport("Iphlpapi.dll", SetLastError = true)]
    public static extern int CreateIpForwardEntry2(ref MIB_IPFORWARD_ROW2 pRoute);


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

    public static void AddRoute(uint interfaceIndex,  IpNetwork ipNetwork, CancellationToken cancellationToken)
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

}