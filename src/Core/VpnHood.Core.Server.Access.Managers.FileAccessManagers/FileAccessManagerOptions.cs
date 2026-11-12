using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Net.Toolkit.Converters;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Server.Access.Managers.FileAccessManagers;

/// <summary>
/// Represents the options of a <see cref="FileAccessManager"/>: the server's configuration, with what goes into the
/// server token its access keys carry.
/// </summary>
/// <remarks>
/// The manager puts its own certificate in place of the configuration's certificates, and, where the UDP endpoints are
/// not set, gives them the TCP endpoints' addresses, on the port the server keeps.
/// </remarks>
public class FileAccessManagerOptions : ServerConfig
{
    /// <summary>
    /// Gets or sets the endpoints the clients reach the server at, or <see langword="null"/> for its TCP endpoints;
    /// none may be an "any" address.
    /// </summary>
    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[]? PublicEndPoints { get; set; }

    /// <summary>Gets or sets the password of the server's certificate file, or <see langword="null"/> for none.</summary>
    public string? SslCertificatesPassword { get; set; }

    /// <summary>
    /// Gets or sets the port the server token gives with the host name, or <see langword="null"/> for the first public
    /// endpoint's.
    /// </summary>
    public int? HostPort { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the certificate's host name is one a client can check as a web browser
    /// does; otherwise, the server token carries the certificate's hash.
    /// </summary>
    public bool IsValidHostName { get; set; }

    /// <summary>Gets or sets the URLs a client may read an updated server token from. The default is none.</summary>
    public string[] ServerTokenUrls { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether a new session's answer carries the access key, so a client takes an
    /// updated server token from it; otherwise, only the URLs update it. The default is <see langword="true"/>.
    /// </summary>
    public bool ReplyAccessKey { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the manager may ask services on the network for the server's location,
    /// for the server token, where its folder keeps none. The default is <see langword="true"/>.
    /// </summary>
    public bool UseExternalLocationService { get; set; } = true;
    internal bool IsUnitTest { get; init; }

    /// <summary>
    /// Gets the first of <see cref="ServerTokenUrls"/>, under its older name, or sets it as the only one where
    /// <see cref="ServerTokenUrls"/> is empty; a set otherwise changes nothing. A set logs a warning either way.
    /// </summary>
    [Obsolete("Use ServerTokenUrls. Version 558 or upper.")]
    public string? ServerTokenUrl {
        get => ServerTokenUrls.FirstOrDefault();
        set {
            if (VhUtils.IsNullOrEmpty(ServerTokenUrls))
                ServerTokenUrls = value != null ? [value] : [];

            VhLogger.Instance.LogWarning("Warning: ServerTokenUrl is obsoleted. Use ServerTokenUrls.");
        }
    }
}