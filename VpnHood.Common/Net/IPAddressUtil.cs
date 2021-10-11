using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;

namespace VpnHood.Common.Net
{
    public static class IPAddressUtil
    {
        public static async Task<IPAddress[]> GetPrivateIpAddresses()
        {
            var ret = new List<IPAddress>();

            var ipV4Task = GetPrivateIpAddress(AddressFamily.InterNetwork);
            var ipV6Task = GetPrivateIpAddress(AddressFamily.InterNetworkV6);
            await Task.WhenAll(ipV4Task, ipV6Task);

            if (ipV4Task.Result != null) ret.Add(ipV4Task.Result);
            if (ipV6Task.Result != null) ret.Add(ipV6Task.Result);

            return ret.ToArray();
        }

        public static async Task<IPAddress[]> GetPublicIpAddresses()
        {
            var ret = new List<IPAddress>();

            var ipV4Task = GetPublicIpAddress(AddressFamily.InterNetwork);
            var ipV6Task = GetPublicIpAddress(AddressFamily.InterNetworkV6);
            await Task.WhenAll(ipV4Task, ipV6Task);

            if (ipV4Task.Result != null) ret.Add(ipV4Task.Result);
            if (ipV6Task.Result != null) ret.Add(ipV6Task.Result);

            return ret.ToArray();
        }

        public static async Task<IPAddress?> GetPrivateIpAddress(AddressFamily addressFamily)
        {
            try
            {

                var remoteIp = addressFamily == AddressFamily.InterNetwork
                ? IPAddress.Parse("8.8.8.8")
                : IPAddress.Parse("2001:4860:4860::8888");

                using var socket = new Socket(addressFamily, SocketType.Dgram, ProtocolType.Udp);
                await socket.ConnectAsync(remoteIp, 53);
                var endPoint = (IPEndPoint)socket.LocalEndPoint;
                return endPoint.Address;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<IPAddress?> GetPublicIpAddress(AddressFamily addressFamily)
        {
            try
            {
                var url = addressFamily == AddressFamily.InterNetwork
                    ? "https://api.ipify.org?format=json"
                    : "https://api64.ipify.org?format=json";

                using var httpClient = new HttpClient();
                var json = await httpClient.GetStringAsync(url);
                var document = JsonDocument.Parse(json);
                var ipString = document.RootElement.GetProperty("ip").GetString();
                var ipAddress = IPAddress.Parse(ipString ?? throw new InvalidOperationException());
                return (ipAddress.AddressFamily == addressFamily) ? ipAddress : null;
            }
            catch
            {
                return null;
            }
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