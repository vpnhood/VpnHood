using System;
using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos;

public class ServerUpdateParams
{
    public Patch<string>? ServerName { get; set; }
    public Patch<Guid?>? ServerFarmId { get; set; }
    public Patch<bool>? GenerateNewSecret { get; set; }
    public Patch<bool>? AutoConfigure { get; set; }
    public Patch<AccessPoint[]>? AccessPoints { get; set; }
}
