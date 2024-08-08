﻿using GrayMint.Common.Utils;
using VpnHood.AccessServer.Dtos.AccessPoints;

namespace VpnHood.AccessServer.Dtos.Servers;

public class ServerUpdateParams
{
    public Patch<string>? ServerName { get; set; }
    public Patch<Guid>? ServerFarmId { get; set; }
    public Patch<bool>? GenerateNewSecret { get; set; }
    public Patch<bool>? AutoConfigure { get; set; }
    public Patch<AccessPoint[]>? AccessPoints { get; set; }
    public Patch<bool>? AllowInAutoLocation { get; set; }
    public Patch<Uri?>? HostPanelUrl { get; set; }
}