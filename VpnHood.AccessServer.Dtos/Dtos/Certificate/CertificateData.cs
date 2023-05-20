using System;
using System.Collections.Generic;

namespace VpnHood.AccessServer.Dtos;

public class CertificateData
{
    public required Certificate Certificate { get; set; }
    public IEnumerable<IdName<Guid>>? ServerFarms { get; set; }
    public CertificateSummary? Summary { get; set; }
}