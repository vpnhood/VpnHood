using VpnHood.Server.Configurations;

namespace VpnHood.Server.Providers.FileAccessServerProvider;

public class FileAccessServerOptions : ServerConfig
{
    public string? SslCertificatesPassword { get; set; }
}