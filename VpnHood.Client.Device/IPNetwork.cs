using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;

namespace VpnHood.Client.Device
{
    [JsonConverter(typeof(IpNetworkConverter))]
    public class IpNetwork
    {
        private readonly long _firstIpAddressLong;
        private readonly long _lastIpAddressLong;

        public IpNetwork(IPAddress prefix, int prefixLength = 32)
        {
            if (prefix.AddressFamily != AddressFamily.InterNetwork)
                throw new NotSupportedException("IPv6 is not supported");

            Prefix = prefix;
            PrefixLength = prefixLength;

            var mask = (uint) ~(0xFFFFFFFFL >> prefixLength);
            _firstIpAddressLong = IpAddressToLong(Prefix) & mask;
            _lastIpAddressLong = _firstIpAddressLong | ~mask;
        }

        public IPAddress Prefix { get; }
        public int PrefixLength { get; }
        public IPAddress FirstIpAddress => IpAddressFromLong(_firstIpAddressLong);
        public IPAddress LastIpAddress => IpAddressFromLong(_lastIpAddressLong);
        public long Total => _lastIpAddressLong - _firstIpAddressLong + 1;

        public static IpNetwork[] LocalNetworks { get; } =
        {
            Parse("10.0.0.0/8"),
            Parse("172.16.0.0/12"),
            Parse("192.168.0.0/16"),
            Parse("169.254.0.0/16")
        };

        public static IpNetwork[] FromIpRange(IpRange ipRange)
        {
            return FromIpRange(ipRange.FirstIpAddress, ipRange.LastIpAddress);
        }

        public static IpNetwork[] FromIpRange(IPAddress firstIpAddress, IPAddress lastIpAddress)
        {
            return FromIpRange(IpAddressToLong(firstIpAddress), IpAddressToLong(lastIpAddress));
        }

        public static IpNetwork[] FromIpRange(long firstIpAddressLong, long lastIpAddressLong)
        {
            var result = new List<IpNetwork>();
            while (lastIpAddressLong >= firstIpAddressLong)
            {
                byte maxSize = 32;
                while (maxSize > 0)
                {
                    var mask = Mask(maxSize - 1);
                    var maskBase = firstIpAddressLong & mask;

                    if (maskBase != firstIpAddressLong)
                        break;

                    maxSize--;
                }

                var x = Math.Log(lastIpAddressLong - firstIpAddressLong + 1) / Math.Log(2);
                var maxDiff = (byte) (32 - Math.Floor(x));
                if (maxSize < maxDiff) maxSize = maxDiff;
                var ipAddress = IpAddressFromLong(firstIpAddressLong);
                result.Add(new IpNetwork(ipAddress, maxSize));
                firstIpAddressLong += (long) Math.Pow(2, 32 - maxSize);
            }

            return result.ToArray();
        }

        private static long Mask(int s)
        {
            return (long) (Math.Pow(2, 32) - Math.Pow(2, 32 - s));
        }

        public static long IpAddressToLong(IPAddress ipAddress)
        {
            var bytes = ipAddress.GetAddressBytes();
            return ((long) bytes[0] << 24) | ((long) bytes[1] << 16) | ((long) bytes[2] << 8) | bytes[3];
        }

        public static IPAddress IpAddressFromLong(long ipAddress)
        {
            return new IPAddress((uint) IPAddress.NetworkToHostOrder((int) ipAddress));
        }

        public IpNetwork[] Invert()
        {
            return Invert(new[] {this});
        }

        public static IpNetwork Parse(string value)
        {
            try
            {
                var parts = value.Split('/');
                return new IpNetwork(IPAddress.Parse(parts[0]), int.Parse(parts[1]));
            }
            catch
            {
                throw new FormatException($"Could not parse IPNetwork from: {value}!");
            }
        }

        public static IOrderedEnumerable<IpNetwork> Sort(IEnumerable<IpNetwork> ipNetworks)
        {
            return ipNetworks.OrderBy(x => x._firstIpAddressLong);
        }

        public static IpNetwork[] Invert(IEnumerable<IpNetwork> ipNetworks)
        {
            return FromIpRange(IpRange.Invert(ToIpRange(ipNetworks)));
        }

        public IpRange ToIpRange()
        {
            return new IpRange(FirstIpAddress, LastIpAddress);
        }

        public static IpRange[] ToIpRange(IEnumerable<IpNetwork> ipNetworks)
        {
            return IpRange.Sort(ipNetworks.Select(x => x.ToIpRange()));
        }

        public static IpNetwork[] FromIpRange(IEnumerable<IpRange> ipRanges)
        {
            List<IpNetwork> ipNetworks = new();
            foreach (var ipRange in IpRange.Sort(ipRanges))
                ipNetworks.AddRange(FromIpRange(ipRange));
            return ipNetworks.ToArray();
        }

        public override string ToString()
        {
            return $"{Prefix}/{PrefixLength}";
        }

        public override bool Equals(object obj)
        {
            return obj is IpNetwork ipNetwork &&
                   FirstIpAddress.Equals(ipNetwork.FirstIpAddress) &&
                   LastIpAddress.Equals(ipNetwork.LastIpAddress);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(FirstIpAddress, LastIpAddress);
        }
    }
}