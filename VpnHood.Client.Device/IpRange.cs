using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json.Serialization;

namespace VpnHood.Client.Device
{
    [JsonConverter(typeof(IpRangeConverter))]
    public class IpRange
    {
        public IpRange(IPAddress ipAddress)
            : this(ipAddress, ipAddress)
        {
        }

        public IpRange(long firstIpAddress, long lastIpAddress)
            : this(IPAddressUtil.FromLong(firstIpAddress), IPAddressUtil.FromLong(lastIpAddress))
        {
        }

        public IpRange(IPAddress firstIpAddress, IPAddress lastIpAddress)
        {
            if (firstIpAddress.AddressFamily != lastIpAddress.AddressFamily)
                throw new InvalidOperationException("Both ipAddress must have a same address family!");

            if (IPAddressUtil.Compare(firstIpAddress, lastIpAddress) > 0)
                throw new InvalidOperationException($"{nameof(lastIpAddress)} must be equal or greater than {nameof(firstIpAddress)}");

            FirstIpAddress = firstIpAddress;
            LastIpAddress = lastIpAddress;
        }

        public AddressFamily AddressFamily => FirstIpAddress.AddressFamily;
        public IPAddress FirstIpAddress { get; }
        public IPAddress LastIpAddress { get; }
        public BigInteger Total => new BigInteger(LastIpAddress.GetAddressBytes(), true, true) - new BigInteger(FirstIpAddress.GetAddressBytes(), true, true) + 1;

        public static IpRange[] Sort(IEnumerable<IpRange> ipRanges)
        {
            var sortedRanges = ipRanges.OrderBy(x => x.FirstIpAddress, new IpAddressComparer());
            return Unify(sortedRanges);
        }

        private static IpRange[] Unify(IEnumerable<IpRange> sortedIpRanges)
        {
            List<IpRange> res = new();
            foreach (var ipRange in sortedIpRanges)
            {
                if (res.Count > 0 &&
                    ipRange.AddressFamily == res[^1].AddressFamily &&
                    IPAddressUtil.Compare(IPAddressUtil.Decrement(ipRange.FirstIpAddress), res[^1].LastIpAddress) <= 0)
                {
                    if (IPAddressUtil.Compare(ipRange.LastIpAddress, res[^1].LastIpAddress) > 0)
                        res[^1] = new IpRange(res[^1].FirstIpAddress, ipRange.LastIpAddress);
                }
                else
                {
                    res.Add(ipRange);
                }
            }

            return res.ToArray();
        }

        public static IpRange[] Invert(IpRange[] ipRanges, bool includeIPv4 = true, bool includeIPv6 = true)
        {
            List<IpRange> list = new();

            // IP4
            if (includeIPv4)
            {
                var ipRanges2 = ipRanges.Where(x => x.AddressFamily == AddressFamily.InterNetwork).ToArray();
                if (ipRanges2.Any())
                    list.AddRange(InvertInternal(ipRanges2));
                else
                    list.Add(new IpRange(IPAddressUtil.MinIPv4Value, IPAddressUtil.MaxIPv4Value));
            }

            // IP6
            if (includeIPv6)
            {
                var ipRanges2 = ipRanges.Where(x => x.AddressFamily == AddressFamily.InterNetworkV6).ToArray();
                if (ipRanges2.Any())
                    list.AddRange(InvertInternal(ipRanges2));
                else
                    list.Add(new IpRange(IPAddressUtil.MinIPv6Value, IPAddressUtil.MaxIPv6Value));
            }

            return list.ToArray();
        }


        private static IpRange[] InvertInternal(IEnumerable<IpRange> ipRanges)
        {
            // sort
            var ipRangesSorted = Sort(ipRanges);

            // extract
            List<IpRange> res = new();
            for (var i = 0; i < ipRangesSorted.Length; i++)
            {
                var ipRange = ipRangesSorted[i];
                var minIpValue = ipRange.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddressUtil.MinIPv6Value : IPAddressUtil.MinIPv4Value;
                var maxIpValue = ipRange.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddressUtil.MaxIPv6Value : IPAddressUtil.MaxIPv4Value;

                if (i == 0 && !IPAddressUtil.IsMinValue(ipRange.FirstIpAddress))
                    res.Add(new IpRange(minIpValue, IPAddressUtil.Decrement(ipRange.FirstIpAddress)));
                if (i > 0)
                    res.Add(new IpRange(IPAddressUtil.Increment(ipRangesSorted[i - 1].LastIpAddress), IPAddressUtil.Decrement(ipRange.FirstIpAddress)));
                if (i == ipRangesSorted.Length - 1 && !IPAddressUtil.IsMaxValue(ipRange.LastIpAddress))
                    res.Add(new IpRange(IPAddressUtil.Increment(ipRange.LastIpAddress), maxIpValue));
            }

            return res.ToArray();
        }

        public static IpRange Parse(string value)
        {
            var items = value.Replace("to", "-").Split('-');
            if (items.Length == 1)
                return new IpRange(IPAddress.Parse(items[0].Trim()));
            if (items.Length == 2)
                return new IpRange(IPAddress.Parse(items[0].Trim()), IPAddress.Parse(items[1].Trim()));
            throw new FormatException($"Could not parse {nameof(IpRange)} from: {value}!");
        }

        public override string ToString()
        {
            return $"{FirstIpAddress}-{LastIpAddress}";
        }

        public override bool Equals(object obj)
        {
            return obj is IpRange ipRange &&
                   FirstIpAddress.Equals(ipRange.FirstIpAddress) &&
                   LastIpAddress.Equals(ipRange.LastIpAddress);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(FirstIpAddress, LastIpAddress);
        }

        public bool IsInRange(IPAddress ipAddress)
        {
            return
                IPAddressUtil.Compare(ipAddress, FirstIpAddress) >= 0 &&
                IPAddressUtil.Compare(ipAddress, LastIpAddress) <= 0;
        }

        /// <summary>
        ///     Search in ipRanges using binary search
        /// </summary>
        /// <param name="sortedIpRanges">a sorted ipRanges</param>
        /// <param name="ipAddress">search value</param>
        /// <returns></returns>
        public static bool IsInRangeFast(IpRange[] sortedIpRanges, IPAddress ipAddress)
        {
            var res = Array.BinarySearch(sortedIpRanges, new IpRange(ipAddress, ipAddress),
                new IpRangeSearchComparer());
            return res >= 0 && res < sortedIpRanges.Length;
        }
        private class IpRangeSearchComparer : IComparer<IpRange>
        {
            public int Compare(IpRange x, IpRange y)
            {
                if (IPAddressUtil.Compare(x.FirstIpAddress, y.FirstIpAddress) <= 0 && IPAddressUtil.Compare(x.LastIpAddress, y.LastIpAddress) >= 0) return 0;
                if (IPAddressUtil.Compare(x.FirstIpAddress, y.FirstIpAddress) < 0) return -1;
                return +1;
            }
        }
    }
}