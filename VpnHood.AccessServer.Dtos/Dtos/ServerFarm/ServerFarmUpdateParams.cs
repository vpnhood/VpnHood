using System;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Dtos;

public class ServerFarmUpdateParams
{
    public Patch<string>? ServerFarmName { get; set; }
    public Patch<Guid>? CertificateId { get; set; }
    public Patch<Guid>? ServerProfileId { get; set; }
}