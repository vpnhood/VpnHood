using VpnHood.Server.Access.Configurations;

namespace VpnHood.Server.Access.Managers.File;

public class FileAccessManagerOptions : ServerConfig
{
    public string? SslCertificatesPassword { get; set; }
}