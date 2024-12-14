using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Converters;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Server.Access.Configurations;

namespace VpnHood.Core.Server.Access.Managers.FileAccessManagers;

public class FileAccessManagerOptions : ServerConfig
{
    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[]? PublicEndPoints { get; set; }

    public string? SslCertificatesPassword { get; set; }
    public int? HostPort { get; set; }
    public bool IsValidHostName { get; set; }
    public string[] ServerTokenUrls { get; set; } = [];
    public bool ReplyAccessKey { get; set; } = true; // if false, access tokens will only be updated by url
    public bool UseExternalLocationService { get; set; } = true;

    [Obsolete("Use ServerTokenUrls. Version 558 or upper.")]
    public string? ServerTokenUrl {
        get => ServerTokenUrls.FirstOrDefault();
        set {
            if (VhUtil.IsNullOrEmpty(ServerTokenUrls))
                ServerTokenUrls = value != null ? [value] : [];

            Console.WriteLine("Warning: ServerTokenUrl is obsoleted. Use ServerTokenUrls.");
        }
    }
}