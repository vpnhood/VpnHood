using System.Text.Json.Serialization;

namespace VpnHood.Core.Server.Access.Configurations;

[JsonConverter(typeof(JsonStringEnumConverter<ReverseProxyProvider>))]
public enum ReverseProxyProvider
{
    Cloudflare,
    CloudFront
}
