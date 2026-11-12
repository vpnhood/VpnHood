using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Server.Access.Messaging;

/// <summary>
/// Represents a client's request for a session, as the server passes it to its access manager, with what the server
/// knows of the client.
/// </summary>
public class SessionRequestEx
{
    /// <summary>Gets the ID of the access key the client connects with.</summary>
    public required string TokenId { get; init; }

    /// <summary>Gets what the client says of itself: its ID, version and user agent.</summary>
    public required ClientInfo ClientInfo { get; init; }

    /// <summary>Gets the client's ID, encrypted with the access key's secret, which shows the client holds the key.</summary>
    public required byte[] EncryptedClientId { get; init; }

    /// <summary>
    /// Gets or sets the client's country, or <see langword="null"/>; the server does not set it.
    /// </summary>
    public string? ClientCountry { get; set; }

    /// <summary>
    /// Gets or sets the server's own data for the session, as JSON, which the access manager keeps and gives back with
    /// the session (<see cref="SessionResponseEx.ExtraData"/>).
    /// </summary>
    public required string? ExtraData { get; set; }

    /// <summary>Gets or sets the server location the client asks for, or <see langword="null"/>.</summary>
    public string? ServerLocation { get; set; }

    /// <summary>Gets or sets the plan the client connects with.</summary>
    public ConnectPlanId PlanId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the client takes a redirect to another server. The default is
    /// <see langword="true"/>.
    /// </summary>
    public bool AllowRedirect { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the client's network carries IPv6, or <see langword="null"/> where it
    /// does not say.
    /// </summary>
    public bool? IsIpV6Supported { get; set; }

    /// <summary>Gets or sets the protocol version the session runs on.</summary>
    public int ProtocolVersion { get; set; }

    /// <summary>Gets or sets the premium code the client connects with, or <see langword="null"/>.</summary>
    public string? AccessCode { get; set; }

    /// <summary>Gets or sets the person's review of the app, sent with the connection, or <see langword="null"/>.</summary>
    public UserReview? UserReview { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the person lets the session be reported anonymously: the access manager
    /// reports it only where it is <see langword="true"/>.
    /// </summary>
    public bool AllowAnonymousTracker { get; set; }

    /// <summary>Gets or sets the server's endpoint the client came to.</summary>
    [JsonConverter(typeof(IPEndPointConverter))]
    public required IPEndPoint HostEndPoint { get; set; }

    /// <summary>
    /// Gets or sets the client's endpoint as the server sees it, or <see langword="null"/> from servers of version 739
    /// and before.
    /// </summary>
    [JsonConverter(typeof(IPEndPointConverter))]
    public IPEndPoint? RemoteEndPoint { get; set; }

    /// <summary>
    /// Gets or sets the client's IP: its own address, or the one a reverse proxy the server trusts names in
    /// X-Forwarded-For.
    /// </summary>
    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress? ClientIp { get; set; }
}