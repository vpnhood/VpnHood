using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Tokens;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Common.Messaging;

/// <summary>
/// Represents a server's answer to a session's request: its outcome and what the session has used.
/// </summary>
public class SessionResponse
{
    /// <summary>Gets or sets the outcome.</summary>
    public required SessionErrorCode ErrorCode { get; set; }

    /// <summary>Gets or sets the error's message, or <see langword="null"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ErrorMessage { get; set; }

    /// <summary>Gets or sets what the session has used of its access, or <see langword="null"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AccessUsage? AccessUsage { get; set; }

    /// <summary>
    /// Gets or sets whose newer session took this one's place, with <see cref="SessionErrorCode.SessionSuppressedBy"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SessionSuppressType SuppressedBy { get; set; }

    /// <summary>Gets or sets the server to redirect to, for clients before protocol 10.</summary>
    [Obsolete("Deprecated on protocol 10. User ServerTokens")]
    [JsonConverter(typeof(IPEndPointConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IPEndPoint? RedirectHostEndPoint { get; set; }

    /// <summary>
    /// Gets or sets the server tokens of the servers to redirect to, with <see cref="SessionErrorCode.RedirectHost"/>,
    /// or <see langword="null"/>.
    /// </summary>
    public ServerToken[]? RedirectServerTokens { get; set; }

    /// <summary>Gets or sets the servers to redirect to, for clients before protocol 10.</summary>
    [Obsolete("Deprecated on protocol 10. User ServerTokens")]
    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IPEndPoint[]? RedirectHostEndPoints { get; set; }

    /// <summary>Gets or sets an access key that replaces the client's, or <see langword="null"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? AccessKey { get; set; }

    /// <summary>Gets or sets the code of the country the server places the client in, or <see langword="null"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ClientCountry { get; set; }

    /// <summary>Gets or sets the client's public IP address as the server sees it, or <see langword="null"/>.</summary>
    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress? ClientPublicAddress { get; set; }

    /// <summary>
    /// Gets or sets the reply's revision, meant to grow with each reply the access manager builds for the session;
    /// reserved, as no access manager sends one and no server reads it yet.
    /// </summary>
    // none sends it yet
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long Revision { get; set; }
}