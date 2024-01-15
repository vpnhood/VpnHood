using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;
using VpnHood.Server.Access.Configurations;

namespace VpnHood.Server.Access.Managers.File;

public class FileAccessManagerOptions : ServerConfig
{
    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[]? PublicEndPoints { get; set; }

    public string? SslCertificatesPassword { get; set; }
    public string? ServerTokenUrl { get; set; }
    public bool UseDomain { get; set; }
    public int? HostPort { get; set; }
}