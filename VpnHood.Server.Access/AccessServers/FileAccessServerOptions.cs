using System;
using System.Net;

namespace VpnHood.Server.AccessServers;

public class FileAccessServerOptions : ServerConfig
{
    public FileAccessServerOptions()
        : base(new IPEndPoint[] { new(IPAddress.Any, 443), new(IPAddress.IPv6Any, 443) }, Guid.Empty.ToString())
    {
    }

    public string? SslCertificatesPassword { get; set; }
}