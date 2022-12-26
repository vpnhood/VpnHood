using System;
using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos;

public class AccessPointGroupUpdateParams
{
    public Patch<string?>? AccessPointGroupName {get;set;}
    public Patch<Guid>? CertificateId { get;set;}
}