using System.Net;

namespace VpnHood.Net.Toolkit.Net;

/// <summary>Provides extension members for sequences of ranges and of addresses.</summary>
public static class IpRangeExtension
{
    /// <summary>Provides the conversions of a sequence of ranges.</summary>
    /// <param name="ipRanges">The ranges.</param>
    extension(IEnumerable<IpRange> ipRanges)
    {
        /// <summary>
        /// Sorts the ranges into an <see cref="IpRangeOrderedList"/>, which merges the ranges of a family that overlap
        /// or touch.
        /// </summary>
        /// <returns>The list.</returns>
        /// <exception cref="ArgumentNullException">The sequence is <see langword="null"/>.</exception>
        public IpRangeOrderedList ToOrderedList()
        {
            return new IpRangeOrderedList(ipRanges);
        }

        /// <summary>
        /// Not supported: use <see cref="IpRangeOrderedList.Intersect(IpRangeOrderedList)"/>, which gives the
        /// addresses both sets hold.
        /// </summary>
        /// <param name="second">Not used.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="NotSupportedException">Always.</exception>
        /// <remarks>
        /// It makes <c>ranges.Intersect(other)</c> fail at once, where <see cref="Enumerable.Intersect{TSource}(IEnumerable{TSource}, IEnumerable{TSource})"/>
        /// would keep only the ranges that are equal in both.
        /// </remarks>
        public IEnumerable<IpRange> Intersect(IEnumerable<IpRange> second)
        {
            _ = second;

            // prevent use Linq.Intersect in mistake. it is bug prone.
            throw new NotSupportedException($"Use {nameof(IpRangeOrderedList)}.Intersect.");
        }

        /// <summary>Gets the ranges' text forms, one per line, in their order.</summary>
        /// <returns>The text, its lines separated by <see cref="Environment.NewLine"/>.</returns>
        /// <exception cref="ArgumentNullException">The sequence is <see langword="null"/>.</exception>
        public string ToText()
        {
            return string.Join(Environment.NewLine, ipRanges.Select(x => x.ToString()));
        }
    }

    /// <summary>Provides the conversions of a sequence of addresses.</summary>
    /// <param name="iPAddresses">The addresses.</param>
    extension(IEnumerable<IPAddress> iPAddresses)
    {
        /// <summary>Converts each address to a range that holds it alone.</summary>
        /// <returns>The ranges, in the addresses' order, made as the result is enumerated.</returns>
        /// <exception cref="ArgumentNullException">The sequence is <see langword="null"/>.</exception>
        public IEnumerable<IpRange> ToIpRanges()
        {
            return iPAddresses.Select(x => new IpRange(x));
        }

        /// <summary>
        /// Sorts the addresses into an <see cref="IpRangeOrderedList"/>, which merges consecutive addresses into one
        /// range.
        /// </summary>
        /// <returns>The list.</returns>
        /// <exception cref="ArgumentNullException">The sequence is <see langword="null"/>.</exception>
        public IpRangeOrderedList ToOrderedIpRanges()
        {
            return new IpRangeOrderedList(iPAddresses.ToIpRanges());
        }
    }
}