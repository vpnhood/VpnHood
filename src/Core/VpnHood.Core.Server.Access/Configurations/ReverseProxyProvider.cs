using System.Text.Json.Serialization;

namespace VpnHood.Core.Server.Access.Configurations;

/// <summary>
/// Specifies a reverse proxy provider whose published addresses the server may trust to name the client's IP in
/// X-Forwarded-For.
/// </summary>
/// <remarks>In JSON it is a name.</remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ReverseProxyProvider>))]
public enum ReverseProxyProvider
{
    /// <summary>Cloudflare.</summary>
    Cloudflare,

    /// <summary>
    /// Amazon CloudFront: not trusted by default, as edge code on any AWS account may write X-Forwarded-For, and
    /// CloudFront marks none of it.
    /// </summary>
    CloudFront
}
