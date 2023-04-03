using System;
using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos;

public class TeamUpdateUserParam
{
    public Patch<Guid>? RoleId { get; set; }
}