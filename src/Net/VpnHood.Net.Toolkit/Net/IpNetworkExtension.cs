namespace VpnHood.Net.Toolkit.Net;

/// <summary>Provides extension members for sequences of networks.</summary>
public static class IpNetworkExtension
{
    /// <summary>Provides the conversions of a sequence of networks.</summary>
    /// <param name="ipNetworks">The networks.</param>
    extension(IEnumerable<IpNetwork> ipNetworks)
    {
        /// <summary>
        /// Sorts the networks and merges those that overlap or touch, as <see cref="IpRangeOrderedList"/> does, into the
        /// fewest networks per merged range.
        /// </summary>
        /// <returns>
        /// The networks, in <see cref="IPAddressUtil.Compare(System.Net.IPAddress, System.Net.IPAddress)"/>'s order of
        /// their first addresses.
        /// </returns>
        /// <exception cref="ArgumentNullException">The sequence is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// A network reaches from other IPv6 addresses into the IPv4-mapped ones, as <c>::/80</c> does, whose range's
        /// ends then compare out of order.
        /// </exception>
        /// <remarks>
        /// The count may change: <c>10.0.0.0/25</c> and <c>10.0.0.128/25</c> come back as <c>10.0.0.0/24</c>.
        /// </remarks>
        public IOrderedEnumerable<IpNetwork> SortAndMerge()
        {
            return ipNetworks
                .ToIpRanges()
                .ToIpNetworks();
        }

        /// <summary>
        /// Converts the networks to an <see cref="IpRangeOrderedList"/>, which sorts their ranges and merges those that
        /// overlap or touch, as its remarks tell.
        /// </summary>
        /// <returns>The list.</returns>
        /// <exception cref="ArgumentNullException">The sequence is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// A network reaches from other IPv6 addresses into the IPv4-mapped ones, as <c>::/80</c> does, whose range's
        /// ends then compare out of order.
        /// </exception>
        public IpRangeOrderedList ToIpRanges()
        {
            return ipNetworks
                .Select(x => x.ToIpRange())
                .ToOrderedList();
        }
    }

    /// <summary>
    /// Provides the checks of a sorted and merged sequence of networks, as <c>SortAndMerge</c> and
    /// <see cref="IpRangeOrderedList.ToIpNetworks"/> give it.
    /// </summary>
    /// <param name="ipNetworks">The networks.</param>
    extension(IOrderedEnumerable<IpNetwork> ipNetworks)
    {
        /// <summary>
        /// Determines whether the networks are <c>0.0.0.0/0</c> and <c>::/0</c>, in that order: every address.
        /// </summary>
        /// <returns><see langword="true"/> if they are; otherwise, <see langword="false"/>.</returns>
        /// <remarks>
        /// Networks that hold every address in smaller pieces, as <c>0.0.0.0/1</c> and <c>128.0.0.0/1</c> do, count once
        /// merged.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The sequence is <see langword="null"/>.</exception>
        public bool IsAll()
        {
            return ipNetworks.SequenceEqual(IpNetwork.All);
        }

        /// <summary>
        /// Determines whether the networks are <c>0.0.0.0/0</c> alone: every IPv4 address and no IPv6 one.
        /// </summary>
        /// <returns><see langword="true"/> if they are; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="ArgumentNullException">The sequence is <see langword="null"/>.</exception>
        public bool IsAllV4()
        {
            return ipNetworks.SequenceEqual([IpNetwork.AllV4]);
        }

        /// <summary>
        /// Determines whether the networks are <c>::/0</c> alone: every IPv6 address and no IPv4 one.
        /// </summary>
        /// <returns><see langword="true"/> if they are; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="ArgumentNullException">The sequence is <see langword="null"/>.</exception>
        public bool IsAllV6()
        {
            return ipNetworks.SequenceEqual([IpNetwork.AllV6]);
        }
    }
}