using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos;

public class UserUpdateParams
{
    public Patch<int>? MaxProjects { get; set; }
}