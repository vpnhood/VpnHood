using System.Net;

namespace VpnHood.Core.Client.Abstractions;

/// <summary>
/// Represents a server token's endpoints, with the ones its host name gave among them.
/// </summary>
/// <remarks>
/// A session whose IPv4 endpoint the name gave asks the name again each time it starts to wait, to learn whether the
/// server moved.
/// </remarks>
public class ResolvedHostEndPoints
{
    /// <summary>
    /// Gets the endpoints, in the order the strategy put them, without repeats but for
    /// <see cref="VpnHood.Core.Common.Tokens.EndPointStrategy.IpOnly"/>'s, the token's list as it is.
    /// </summary>
    public required IReadOnlyList<IPEndPoint> EndPoints { get; init; }

    /// <summary>
    /// Gets the endpoints of the host name's addresses, asked or known already; empty where neither gave any, as where
    /// the name was not asked or did not answer.
    /// </summary>
    public required IReadOnlyList<IPEndPoint> NameEndPoints { get; init; }
}
