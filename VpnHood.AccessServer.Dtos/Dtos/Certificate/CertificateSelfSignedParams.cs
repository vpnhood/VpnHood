using System;

namespace VpnHood.AccessServer.Dtos;

public class CertificateSelfSignedParams
{
    public string? SubjectName { get; set; }
    public DateTime? ExpirationTime { get; set; }
}