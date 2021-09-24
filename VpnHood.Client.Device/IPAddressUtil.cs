using System;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using PacketDotNet;

namespace VpnHood.Client.Device
{
    public static class IPAddressUtil
    {
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
    }
}