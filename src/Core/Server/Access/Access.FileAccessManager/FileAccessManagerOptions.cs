using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Toolkit.Converters;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Server.Access.Managers.FileAccessManagement;

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
    public bool IsUnitTest { get; init; }

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