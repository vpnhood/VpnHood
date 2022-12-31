using System;
using System.Net;

namespace VpnHood.Server.Providers.FileAccessServerProvider;

public class FileAccessServerOptions : ServerConfig
{
    public string? SslCertificatesPassword { get; set; }
}