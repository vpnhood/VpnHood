using System.Net;

namespace VpnHood.Net.Toolkit.Net;

// ReSharper disable once InconsistentNaming
/// <summary>
/// Provides the order of IP addresses that <see cref="IPAddressUtil.Compare"/> gives.
/// </summary>
/// <remarks>
/// A null comes first; an IPv4-mapped IPv6 address counts as its IPv4 address; IPv4 addresses come before IPv6 ones; and
/// addresses compare by their bytes, without scope IDs, so two IPv6 addresses of different scopes can compare equal.
/// </remarks>
public class IPAddressComparer : IComparer<IPAddress>
{
    /// <inheritdoc/>
    public int Compare(IPAddress? x, IPAddress? y)
        => IPAddressUtil.Compare(x, y);
}