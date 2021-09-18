using System;
using System.Net;
using System.Net.Sockets;

namespace VpnHood.Client.Device
{
    public static class IPAddressUtil
    {
        private static bool IsSupported(AddressFamily addressFamily)
        {
            return addressFamily
                is AddressFamily.InterNetworkV6
                or AddressFamily.InterNetwork;
        }

        private static void Verify(AddressFamily addressFamily)
        {
            if (!IsSupported(addressFamily))
                throw new NotSupportedException($"{addressFamily} is not supported!");
        }

        private static void Verify(IPAddress ipAddress)
        {
            Verify(ipAddress.AddressFamily);
        }

        public static int Compare(IPAddress ipAddress1, IPAddress ipAddress2)
        {
            Verify(ipAddress1);
            Verify(ipAddress2);

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
        
        public static IPAddress FromLong(long ipAddress)
        {
            return new IPAddress((uint)IPAddress.NetworkToHostOrder((int)ipAddress));
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
                AddressFamily.InterNetworkV6 => ipAddress.Equals(MinIPv6Value),
                AddressFamily.InterNetwork => ipAddress.Equals(MinIPv4Value),
                _ => throw new NotSupportedException($"{ipAddress.AddressFamily} is not supported!")
            };
        }

    }
}