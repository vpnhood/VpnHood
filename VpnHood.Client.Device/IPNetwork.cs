using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json.Serialization;

namespace VpnHood.Client.Device
{
    [JsonConverter(typeof(IpNetworkConverter))]
    public class IpNetwork
    {
        private readonly long _firstIpAddressLong;
        private readonly long _lastIpAddressLong;

        public static IpNetwork[] LocalNetworks { get; } = new IpNetwork[] {
            Parse("10.0.0.0/8"),
            Parse("172.16.0.0/12"),
            Parse("192.168.0.0/16"),
            Parse("169.254.0.0/16"),
        };

        public static IpNetwork[] FromIpRange(IpRange ipRange)
          => FromIpRange(ipRange.FirstIpAddress, ipRange.LastIpAddress);

        public static IpNetwork[] FromIpRange(IPAddress firstIpAddress, IPAddress lastIpAddress)
            => FromIpRange(IpAddressToLong(firstIpAddress), IpAddressToLong(lastIpAddress));

        public static IpNetwork[] FromIpRange(long firstIpAddressLong, long lastIpAddressLong)
        {
            var result = new List<IpNetwork>();
            while (lastIpAddressLong >= firstIpAddressLong)
            {
                byte maxSize = 32;
                while (maxSize > 0)
                {
                    long mask = IMask(maxSize - 1);
                    long maskBase = firstIpAddressLong & mask;

                    if (maskBase != firstIpAddressLong)
                        break;

                    maxSize--;
                }
                double x = Math.Log(lastIpAddressLong - firstIpAddressLong + 1) / Math.Log(2);
                byte maxDiff = (byte)(32 - Math.Floor(x));
                if (maxSize < maxDiff)
                {
                    maxSize = maxDiff;
                }
                var ipAddress = IpAddressFromLong(firstIpAddressLong);
                result.Add(new IpNetwork(ipAddress, maxSize));
                firstIpAddressLong += (long)Math.Pow(2, 32 - maxSize);
            }
            return result.ToArray();
        }

        private static long IMask(int s)
        {
            return (long)(Math.Pow(2, 32) - Math.Pow(2, 32 - s));
        }

        public static long IpAddressToLong(IPAddress ipAddress)
        {
            var bytes = ipAddress.GetAddressBytes();
            return ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
        }

        public static IPAddress IpAddressFromLong(long ipAddress)
            => new((uint)IPAddress.NetworkToHostOrder((int)ipAddress));

        public IpNetwork(IPAddress prefix, int prefixLength = 32)
        {
            if (prefix.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                throw new NotSupportedException("IPv6 is not supported");

            Prefix = prefix;
            PrefixLength = prefixLength;

            var mask = (uint)~(0xFFFFFFFFL >> prefixLength);
            _firstIpAddressLong = IpAddressToLong(Prefix) & mask;
            _lastIpAddressLong = _firstIpAddressLong | ~mask;
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
                throw new FormatException($"Could not parse IPNetwork from {value}");
            }
        }

        public static IpNetwork[] InvertRange(IpNetwork[] ipNetworks)
        {
            // sort
            ipNetworks = ipNetworks.OrderBy(x => x._firstIpAddressLong).ToArray();

            // extract
            List<IpNetwork> ret = new();
            for (var i = 0; i < ipNetworks.Length; i++)
            {
                var ipNetwork = ipNetworks[i];

                if (i > 0 && ipNetwork._firstIpAddressLong <= ipNetworks[i - 1]._lastIpAddressLong)
                    throw new ArgumentException($"The networks should not have any intersection! {ipNetworks[i - 1]}, {ipNetwork}", nameof(ipNetworks));

                if (i == 0 && ipNetwork._firstIpAddressLong != 0) ret.AddRange(FromIpRange(0, ipNetwork._firstIpAddressLong - 1));
                if (i > 0 && i < ipNetworks.Length - 1) ret.AddRange(FromIpRange(ipNetworks[i - 1]._lastIpAddressLong + 1, ipNetwork._firstIpAddressLong - 1));
                if (i == ipNetworks.Length - 1 && ipNetwork._lastIpAddressLong != 0xFFFFFFFF) ret.AddRange(FromIpRange(ipNetwork._lastIpAddressLong + 1, 0xFFFFFFFF));
            }

            return ret.ToArray();
        }

        public IpRange ToIpRange() => new (FirstIpAddress, LastIpAddress);

        public static IpRange[] ToIpRange(IpNetwork[] ipNetworks)
        {
            List<IpRange> ret = new();

            // sort
            ipNetworks = ipNetworks.OrderBy(x => x._firstIpAddressLong).ToArray();

            for (var i = 0; i < ipNetworks.Length; i++)
            {
                var ipNetwork = ipNetworks[i];

                // remove extra networks
                if (ipNetworks.Any(x => ipNetwork._firstIpAddressLong > x._firstIpAddressLong && ipNetwork._lastIpAddressLong < x._lastIpAddressLong))
                    continue;

                if (ret.Count > 0 && ipNetwork._firstIpAddressLong == IpAddressToLong(ret[^1].LastIpAddress) + 1)
                    ret[^1] = new(ret[^1].FirstIpAddress, ipNetwork.LastIpAddress);
                else
                    ret.Add(new(ipNetwork.FirstIpAddress, ipNetwork.LastIpAddress));
            }

            return ret.ToArray();
        }

        public override string ToString() => $"{Prefix}/{PrefixLength}";

        public IPAddress Prefix { get; }
        public int PrefixLength { get; }
        public IPAddress FirstIpAddress => IpAddressFromLong(_firstIpAddressLong);
        public IPAddress LastIpAddress => IpAddressFromLong(_lastIpAddressLong);
        public long Total => _lastIpAddressLong - _firstIpAddressLong + 1;
    }
}
