using GrayMint.Common;

namespace VpnHood.AccessServer.Dtos;

public class UserUpdateParams
{
    public Patch<int>? MaxProjects { get; set; }
}