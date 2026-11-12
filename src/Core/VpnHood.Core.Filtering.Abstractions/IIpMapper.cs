using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Filtering.Abstractions;

/// <summary>
/// Defines a map of endpoints: from the one a flow is sent to, to the one it reaches instead, and back for its
/// replies.
/// </summary>
/// <remarks>
/// The server maps the destinations of the packets and TCP streams it forwards, and the sources of their replies; the
/// client maps the flows it sends around the tunnel. The tests use it to reach local servers in place of remote ones.
/// </remarks>
public interface IIpMapper : IDisposable
{
    /// <summary>Maps the endpoint a flow is sent to, to the one it reaches instead.</summary>
    /// <param name="protocol">The flow's protocol.</param>
    /// <param name="hostEndPoint">The endpoint the flow is sent to.</param>
    /// <param name="newEndPoint">When this method returns <see langword="true"/>, the endpoint the flow reaches.</param>
    /// <returns><see langword="true"/> if the endpoint is mapped; otherwise, <see langword="false"/>.</returns>
    bool ToHost(IpProtocol protocol, IpEndPointValue hostEndPoint, out IpEndPointValue newEndPoint);

    /// <summary>
    /// Maps the endpoint a reply comes from back to the one its flow was sent to: the reverse of <see cref="ToHost"/>.
    /// </summary>
    /// <param name="protocol">The reply's protocol.</param>
    /// <param name="hostEndPoint">The endpoint the reply comes from.</param>
    /// <param name="newEndPoint">When this method returns <see langword="true"/>, the endpoint the flow was sent to.</param>
    /// <returns><see langword="true"/> if the endpoint is mapped; otherwise, <see langword="false"/>.</returns>
    bool FromHost(IpProtocol protocol, IpEndPointValue hostEndPoint, out IpEndPointValue newEndPoint);
}
