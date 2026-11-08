using System.Net;

namespace VpnHood.Net.Toolkit.Net;

// ReSharper disable once InconsistentNaming
/// <summary>
/// Provides the conversion of an <see cref="IPEndPoint"/> to an <see cref="IpEndPointValue"/>.
/// </summary>
public static class IPEndPointExtensions
{
    /// <summary>Provides the conversion of an endpoint.</summary>
    /// <param name="ipEndPoint">The endpoint.</param>
    extension(IPEndPoint ipEndPoint)
    {
        /// <summary>Converts the endpoint to a value with the same address and port.</summary>
        /// <returns>The value.</returns>
        public IpEndPointValue ToValue() => new(ipEndPoint.Address, ipEndPoint.Port);
    }
}