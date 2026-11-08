using System.Net;

namespace VpnHood.Core.Client.Abstractions;

// A token's endpoints, with the ones its name gave among them: none where the name was not asked or did not answer.
// A session whose endpoint the name gave watches the name for a move (ServerAddressCheck).
public class ResolvedHostEndPoints
{
    public required IReadOnlyList<IPEndPoint> EndPoints { get; init; }
    public required IReadOnlyList<IPEndPoint> NameEndPoints { get; init; }
}
