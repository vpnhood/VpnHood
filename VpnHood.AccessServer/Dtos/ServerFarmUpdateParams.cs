using System;
using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos;

public class ServerFarmUpdateParams
{
    public Patch<string?>? ServerFarmName {get;set;}
    public Patch<Guid>? CertificateId { get;set;}
}