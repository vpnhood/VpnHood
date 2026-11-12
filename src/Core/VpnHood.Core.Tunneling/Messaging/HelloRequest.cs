using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Tunneling.Messaging;

/// <summary>
/// Represents a client's request for a session, the first request of a connect: the access key it connects with and
/// what it asks for.
/// </summary>
public class HelloRequest()
    : ClientRequest((byte)Messaging.RequestCode.Hello)
{
    /// <summary>Gets the ID of the access key the client connects with.</summary>
    public required string TokenId { get; init; }

    /// <summary>Gets what the client says of itself: its ID, version and user agent.</summary>
    public required ClientInfo ClientInfo { get; init; }

    /// <summary>Gets the client's ID, encrypted with the access key's secret, which shows the client holds the key.</summary>
    public required byte[] EncryptedClientId { get; init; }

    /// <summary>
    /// Gets the server location the client asks for, as <c>countryCode/region/city</c>, or <see langword="null"/>.
    /// </summary>
    public string? ServerLocation { get; init; }

    /// <summary>Gets the plan the client connects with.</summary>
    public ConnectPlanId PlanId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the client takes a redirect to another server. The default is
    /// <see langword="true"/>.
    /// </summary>
    public bool AllowRedirect { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the client's VPN adapter takes IPv6, or <see langword="null"/> where the client
    /// does not say.
    /// </summary>
    public bool? IsIpV6Supported { get; init; }

    /// <summary>Gets or sets the premium code the client connects with, or <see langword="null"/>.</summary>
    public string? AccessCode { get; set; }

    /// <summary>Gets the person's review of the app, sent with the connection, or <see langword="null"/>.</summary>
    public UserReview? UserReview { get; init; }

    /// <summary>
    /// Gets the largest packet the client's tunnel takes; the session's tunnel carries none larger. The default is
    /// <see cref="TransportDefaults.MtuClient"/>.
    /// </summary>
    public int Mtu { get; init; } = TransportDefaults.MtuClient;

    /// <summary>
    /// Gets a value indicating whether the person lets the session be reported anonymously, as they chose at the
    /// connect; a client from before it sends none, read as <see langword="false"/>.
    /// </summary>
    public bool AllowAnonymousTracker { get; init; }
}