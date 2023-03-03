using System;
using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos.ServerFarmDtos;

public class ServerFarmUpdateParams
{
    public Patch<string?>? ServerFarmName { get; set; }
    public Patch<Guid>? CertificateId { get; set; }
}