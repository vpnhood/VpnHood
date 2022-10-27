using System;
using GrayMint.Common.Client;

namespace VpnHood.AccessServer.Dtos;

public class AccessPointGroupUpdateParams
{
    public Patch<string?>? AccessPointGroupName {get;set;}
    public Patch<Guid>? CertificateId { get;set;}
}